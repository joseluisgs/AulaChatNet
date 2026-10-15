using System.Text;
using AulaChatNet.Client.Terminal.Comandos;
using AulaChatNet.Client.Terminal.Configuracion;
using AulaChatNet.Client.Terminal.Consola;
using AulaChatNet.Client.Terminal.Servicios;
using AulaChatNet.Client.Terminal.Sesion;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

// ============================================================================
// 🔧 CONFIGURACIÓN (appsettings.json copiado al binario por el .csproj)
// ============================================================================

var configuracion = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

// Sección Chat: la MISMA que usa el servidor (canales y límites compartidos)
var chat = configuracion.GetSection(ChatConfig.Seccion).Get<ChatConfig>() ?? new ChatConfig();

// Sección Terminal: URL del servidor y si se recuerda la sesión
var terminal = configuracion.GetSection(TerminalConfig.Seccion).Get<TerminalConfig>()
               ?? new TerminalConfig();

// Última sesión guardada en %APPDATA% (nick y canal por defecto)
var storeSesion = new SesionStore();
var sesionGuardada = terminal.RecordarSesion ? storeSesion.Cargar() : null;

// ============================================================================
// 🖼️ PANTALLA DE ENTRADA: banner + ayuda
// ============================================================================

Banner.Mostrar();
Ayuda.MostrarGeneral();

// ============================================================================
// ❓ NICK Y CANAL (con defaults de la sesión; validación LOCAL con ChatConfig)
// ============================================================================
// La validación es la misma clase que ejecuta el servidor: si aquí dice que
// vale, allí no puede fallar por longitud... salvo por el nombre duplicado,
// que solo puede decidirlo el servidor (lo verás abajo en el reintento).

var nick = PedirNick(chat, sesionGuardada);

if (nick is null)
{
    AnsiConsole.MarkupLine("[grey]Entrada cerrada (EOF) sin datos. Hasta pronto.[/]");
    return;
}

var canal = PedirCanal(chat, sesionGuardada);

if (canal is null)
{
    AnsiConsole.MarkupLine("[grey]Entrada cerrada (EOF) sin datos. Hasta pronto.[/]");
    return;
}

// ============================================================================
// 🔌 CONEXIÓN (con reintento si el servidor rechaza el nick)
// ============================================================================

var urlHub = $"{terminal.UrlServidor.TrimEnd('/')}{chat.HubRuta}";
using var cliente = new ClienteChat(urlHub, chat);

string? error;

do
{
    error = await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync($"Conectando a {urlHub}...", _ => cliente.ConectarAsync(nick, canal));

    if (error is null)
        break;

    AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");

    // La causa típica es el nick ocupado (dos terminales en el mismo equipo):
    // se vuelve a preguntar SIN reiniciar el programa
    nick = PedirNick(chat, sesion: null);

    if (nick is null)
    {
        AnsiConsole.MarkupLine("[grey]Entrada cerrada (EOF). Hasta pronto.[/]");
        return;
    }
}
while (true);

// Solo se guarda la sesión si la conexión fue correcta: jamás guardamos
// un nick que el servidor haya rechazado
if (terminal.RecordarSesion)
    storeSesion.Guardar(new SesionTerminal(nick, canal));

AnsiConsole.MarkupLine(
    $"[green]●[/] Conectado a [bold]{Markup.Escape(canal)}[/] como [bold]{Markup.Escape(nick)}[/]. " +
    "Escribe [grey]/ayuda[/] para ver los comandos.");
AnsiConsole.WriteLine();

// ============================================================================
// 💬 BUCLE DE CHAT: los mensajes entrantes llegan por Rx mientras leemos
// ============================================================================

// Rx entrega la VISTA completa del canal (BehaviorSubject): o se le AÑADEN
// mensajes (llega la lista anterior + el nuevo) o se REEMPLAZA entera
// (historial de otro canal). Distinguimos ambos casos para pintar solo lo
// nuevo: los mensajes anteriores siguen al principio por MISMA referencia.
IReadOnlyList<MensajeDto> pintados = [];

using var suscripcion = cliente.Mensajes.Subscribe(lista =>
{
    var incremental = EsContinuacion(lista, pintados);

    // Cambio de canal: pantalla limpia y pintamos la vista entera
    if (!incremental && pintados.Count > 0)
        AnsiConsole.Clear();

    for (var i = incremental ? pintados.Count : 0; i < lista.Count; i++)
        PintarMensaje(lista[i]);

    pintados = lista;
});

// Ctrl+C = salida limpia: guardar sesión y despedirse, nunca a lo bruto.
// Estas dos variables se actualizan al cambiar de canal o de nick.
var nickActual = nick;
var canalActual = canal;

Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true; // no matamos el proceso mientras escribe algo

    if (terminal.RecordarSesion)
        storeSesion.Guardar(new SesionTerminal(nickActual, canalActual));

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Saliendo... (Ctrl+C)[/]");
    Environment.Exit(0);
};

var salir = false;

while (!salir)
{
    var linea = LeerChat();

    // EOF: la tubería de entrada se cerró → salida ordenada como /salir
    if (linea is null)
        break;

    switch (ParserComandos.Parse(linea))
    {
        case Comando.Enviar { Texto: var texto } when !string.IsNullOrWhiteSpace(texto):
            error = await cliente.EnviarAsync(texto);

            if (error is not null)
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");

            break;

        case Comando.Enviar:
            // Línea vacía o solo espacios: no se envía nada
            break;

        case Comando.Ayuda { Tema: var tema }:
            Ayuda.MostrarTema(tema);
            break;

        case Comando.Canales:
            MostrarCanales(cliente);
            break;

        case Comando.Canal { Nombre: var nombreCanal }:
            if (await CambiarCanalAsync(cliente, chat, nombreCanal))
                canalActual = cliente.Canal ?? canalActual;

            break;

        case Comando.Nick { Nombre: var nombreNick }:
            if (await CambiarNickAsync(cliente, chat, nombreNick))
                nickActual = nombreNick.Trim();

            break;

        case Comando.Usuarios:
            MostrarUsuarios(cliente);
            break;

        case Comando.Limpiar:
            AnsiConsole.Clear();
            break;

        case Comando.Salir:
            salir = true;
            break;

        case Comando.Desconocido { Palabra: var palabra }:
            AnsiConsole.MarkupLine(
                $"[yellow]Comando no reconocido:[/] {Markup.Escape(palabra)}. " +
                "Escribe [grey]/ayuda[/] para ver todos.");
            break;

        case Comando.Invalido invalido:
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(invalido.Motivo)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(invalido.Uso)}[/]");
            break;
    }
}

// ============================================================================
// 🏁 SALIDA ORDENADA
// ============================================================================

if (terminal.RecordarSesion)
    storeSesion.Guardar(new SesionTerminal(nickActual, canalActual));

await cliente.CerrarAsync();
AnsiConsole.MarkupLine("[grey]Hasta pronto![/]");


// ============================================================================
// 🧩 Funciones auxiliares (al final, como en el Program del servidor)
// ============================================================================

// Lee una línea con soporte dual: en consola INTERACTIVA, el TextPrompt de
// Spectre (ShowDefaultValue pinta el valor por defecto en verde y Enter lo
// acepta); con la entrada REDIRIGIDA —tubería de pruebas o `echo Ana | ...`—
// Spectre no puede leer teclas (ReadKey no existe sin consola), así que se
// usa Console.ReadLine con el mismo contrato: vacío → defecto, EOF → null.
static string? PedirTexto(string etiqueta, string defecto)
{
    if (!Console.IsInputRedirected)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"[grey]{Markup.Escape(etiqueta)}[/]")
                .DefaultValue(defecto));
    }

    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(etiqueta)}[/] ({Markup.Escape(defecto)})");

    var linea = Console.ReadLine();

    if (linea is null)
        return null; // EOF: no hay más entrada

    return string.IsNullOrWhiteSpace(linea) ? defecto : linea.Trim();
}

// Pide el nick en bucle hasta que pase la validación local de ChatConfig.
// Devuelve null solo si la entrada se cerró (EOF): no hay con qué preguntar.
static string? PedirNick(ChatConfig chat, SesionTerminal? sesion)
{
    var defecto = sesion?.Nick ?? "alumno";

    while (true)
    {
        var nick = PedirTexto("Nick", defecto);

        if (nick is null)
            return null;

        nick = nick.Trim();
        var error = chat.ValidarUsuario(nick);

        if (error is null)
            return nick;

        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");
    }
}

// Pide el canal en bucle hasta que exista en la configuración. Si la sesión
// guardada apunta a un canal que ya no existe (alguien editó los canales),
// se cae al canal por defecto: el programa nunca arranca en un callejón.
// Devuelve null solo si la entrada se cerró (EOF).
static string? PedirCanal(ChatConfig chat, SesionTerminal? sesion)
{
    var defecto = sesion is not null && chat.EsCanalValido(sesion.Canal)
        ? sesion.Canal
        : chat.CanalPorDefecto;

    while (true)
    {
        var escrito = PedirTexto("Canal", defecto);

        if (escrito is null)
            return null;

        var canonico = chat.EncontrarCanal(escrito);

        if (canonico is not null)
            return canonico;

        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(chat.ErrorCanalDesconocido(escrito))}[/]");
    }
}

// Lee una línea del chat en vivo (con Canales, mensajes y comandos).
// Devuelve null SOLO con EOF: el bucle principal lo usa para salir
// ordenadamente, igual que si el usuario escribiese /salir.
static string? LeerChat()
{
    if (!Console.IsInputRedirected)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("> ")
                .AllowEmpty()
                .PromptStyle(TemaDiscord.TextoSecundario));
    }

    // ParserComandos.Parse ya recorta la línea por dentro
    return Console.ReadLine();
}

// Comprueba si la lista nueva CONTINÚA a la anterior: mismos mensajes al
// principio (misma referencia) y alguno más. Si no, es un reemplazo
// (historial de otro canal) y hay que repintar desde cero.
static bool EsContinuacion(IReadOnlyList<MensajeDto> nueva, IReadOnlyList<MensajeDto> anterior)
{
    if (nueva.Count < anterior.Count)
        return false;

    for (var i = 0; i < anterior.Count; i++)
    {
        if (!ReferenceEquals(nueva[i], anterior[i]))
            return false;
    }

    return true;
}

// Muestra los canales configurados, marcando el actual con el punto verde.
static void MostrarCanales(ClienteChat cliente)
{
    foreach (var canalDisponible in cliente.Canales)
    {
        var esActual = string.Equals(canalDisponible, cliente.Canal, StringComparison.OrdinalIgnoreCase);
        var marca = esActual ? " [green]● actual[/]" : "";

        AnsiConsole.MarkupLine(
            $"[{TemaDiscord.Blurple.ToMarkup()}]{Markup.Escape(canalDisponible)}[/]{marca}");
    }

    AnsiConsole.WriteLine();
}

// Cambia de canal validando ANTES en local (misma ChatConfig que el servidor).
// Devuelve true si el cambio se hizo (el llamador actualiza la sesión).
static async Task<bool> CambiarCanalAsync(ClienteChat cliente, ChatConfig chat, string nombreCanal)
{
    if (!chat.EsCanalValido(nombreCanal))
    {
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(chat.ErrorCanalDesconocido(nombreCanal))}[/]");

        return false;
    }

    var error = await cliente.CambiarCanalAsync(nombreCanal);

    if (error is not null)
    {
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");

        return false;
    }

    return true;
}

// Cambia el nick validando ANTES en local; el aviso "X ahora se llama Y"
// lo pinta el servidor por el flujo normal de mensajes. Devuelve true si
// el cambio se hizo (el llamador actualiza la sesión).
static async Task<bool> CambiarNickAsync(ClienteChat cliente, ChatConfig chat, string nombreNick)
{
    var error = chat.ValidarUsuario(nombreNick.Trim());

    if (error is null)
        error = await cliente.CambiarNickAsync(nombreNick.Trim());

    if (error is not null)
    {
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");

        return false;
    }

    return true;
}

// Muestra los conectados del canal actual con el punto verde de Discord.
static void MostrarUsuarios(ClienteChat cliente)
{
    var usuarios = cliente.VistaUsuarios;

    if (usuarios.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]Aún no hay nadie conectado en este canal.[/]");
        AnsiConsole.WriteLine();

        return;
    }

    foreach (var usuario in usuarios)
    {
        var hex = TemaDiscord.ColorDe(usuario.Usuario).ToMarkup();

        AnsiConsole.MarkupLine($"[green]●[/] [{hex}]{Markup.Escape(usuario.Usuario)}[/]");
    }

    AnsiConsole.WriteLine();
}

// Pinta un mensaje recibido: el de sistema en gris cursiva, el normal con
// timestamp (UTC, la hora que asigna el servidor), iniciales entre
// corchetes y nombre con su color derivado.
static void PintarMensaje(MensajeDto mensaje)
{
    if (mensaje.Tipo == TipoMensaje.Sistema)
    {
        AnsiConsole.MarkupLine($"[grey italic]{Markup.Escape(mensaje.Texto)}[/]");

        return;
    }

    var hex = TemaDiscord.ColorDe(mensaje.Usuario).ToMarkup();
    var iniciales = TemaDiscord.Iniciales(mensaje.Usuario);

    AnsiConsole.MarkupLine(
        $"[grey]{mensaje.Timestamp:HH:mm}[/] " +
        $"[{hex}][[{iniciales}]][/] " +
        $"[{hex}]{Markup.Escape(mensaje.Usuario)}[/]: {Markup.Escape(mensaje.Texto)}");
}
