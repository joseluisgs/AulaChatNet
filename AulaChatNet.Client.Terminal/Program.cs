using System.Text;
using System.Text.Json;
using AulaChatNet.Client.Terminal.Comandos;
using AulaChatNet.Client.Terminal.Configuracion;
using AulaChatNet.Client.Terminal.Consola;
using AulaChatNet.Client.Terminal.Servicios;
using AulaChatNet.Client.Terminal.Sesion;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Rendering;

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

// Última sesión guardada en %APPDATA% (nick, canal y cuándo fue)
var storeSesion = new SesionStore();
var sesionGuardada = terminal.RecordarSesion ? storeSesion.Cargar() : null;

// ============================================================================
// 🖼️ PANTALLA DE ENTRADA: banner + ayuda
// ============================================================================

Banner.Mostrar();
Ayuda.MostrarGeneral();

// ============================================================================
// 📌 CUÁNTA GENTE HAY AHORA MISMO (antes de pedir el nick)
// ============================================================================
// El servidor publica GET /estadisticas (como una API REST): con eso basta
// para decir cuánta gente hay en el aula ANTES siquiera de registrarse.
// Si el servidor no está, NO se corta el arranque: se avisa y se sigue
// (ya fallará el paso de conexión si toca).
await PintarConectadosInicialesAsync(terminal.UrlServidor, chat);

AnsiConsole.WriteLine();

// ============================================================================
// ❓ BIENVENIDA Y ELECCIÓN DE NICK Y CANAL (entradas guiadas)
// ============================================================================
// Nada por arte de magia: se dice quién era, en qué canal estaba, cuándo fue
// y DÓNDE se mete cada instrucción (van dentro de los propios prompts).
// La validación es la MISMA clase que ejecuta el servidor: si aquí dice que
// vale, allí no puede fallar por longitud... salvo por el nombre duplicado,
// que solo puede decidirlo el servidor.

// Sesión utilizable SOLO si trae nick Y canal: un fichero a medias o con
// campos en blanco no sirve como default (se pediría un nick vacío)
var sesionValida = sesionGuardada is not null
                   && !string.IsNullOrWhiteSpace(sesionGuardada.Nick)
                   && !string.IsNullOrWhiteSpace(sesionGuardada.Canal)
    ? sesionGuardada
    : null;

// Cada caso tiene SU mensaje de bienvenida (jamás todos lo mismo):
if (sesionValida is not null)
{
    // Con fecha si el fichero la trae; sin ella (ficheros antiguos) se
    // DICE en lugar de ocultarlo en silencio
    var cuando = sesionValida.UltimaConexion > DateTime.MinValue
        ? $" [grey]el[/] [bold]{sesionValida.UltimaConexion:dd/MM/yyyy HH:mm}[/]"
        : " [grey](sin fecha guardada)[/]";

    AnsiConsole.MarkupLine(
        $"[grey]👋 ¡Hola de nuevo! Tu última sesión fue[/] [bold]{Markup.Escape(sesionValida.Nick)}[/] " +
        $"[grey]en el canal[/] [bold]{Markup.Escape(sesionValida.Canal)}[/]{cuando}");
}
else if (storeSesion.SesionCorrupta)
{
    // Había sesión pero el fichero no se pudo leer: NO se dice "primera vez"
    AnsiConsole.MarkupLine(
        "[yellow]⚠ No pude leer tu sesión guardada (fichero corrupto): empezamos de cero.[/]");
}
else if (sesionGuardada is not null)
{
    // Fichero legible pero incompleto (sin nick o sin canal)
    AnsiConsole.MarkupLine(
        "[yellow]⚠ Tu sesión guardada está vacía (falta el nick o el canal): empezamos de cero.[/]");
}
else if (terminal.RecordarSesion)
{
    AnsiConsole.MarkupLine("[grey]🙂 Primera vez por aquí: ¿con qué nick entras?[/]");
}
else
{
    AnsiConsole.MarkupLine("[grey]¿Con qué nick entras?[/]");
}

AnsiConsole.WriteLine();

// Los prompts solo reciben default si la sesión era utilizable
var nick = PedirNick(chat, sesionValida);

if (nick is null)
{
    AnsiConsole.MarkupLine("[grey]Entrada cerrada (EOF) sin datos. Hasta pronto.[/]");
    return;
}

var canal = PedirCanal(chat, sesionValida);

if (canal is null)
{
    AnsiConsole.MarkupLine("[grey]Entrada cerrada (EOF) sin datos. Hasta pronto.[/]");
    return;
}

// Confirmación clara de lo que se va a hacer, antes de tocar el servidor
AnsiConsole.MarkupLine(
    $"[grey]→ Entrarás como[/] [bold]{Markup.Escape(nick)}[/] [grey]en[/] [bold]{Markup.Escape(canal)}[/]");
AnsiConsole.WriteLine();

// ============================================================================
// 🔌 CONEXIÓN (con reintento si el servidor rechaza el nick)
// ============================================================================

var urlHub = $"{terminal.UrlServidor.TrimEnd('/')}{chat.HubRuta}";
using var cliente = new ClienteChat(urlHub, chat);

string? error = null;

do
{
    error = await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync($"Conectando a {urlHub}...", _ => cliente.ConectarAsync(nick, canal));

    if (error is null)
        break;

    AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");

    // La causa típica es el nick ocupado (dos terminales en el mismo equipo):
    // se vuelve a preguntar SIN reiniciar el programa, y se dice DÓNDE
    AnsiConsole.MarkupLine(
        $"[grey]No se pudo entrar como ese nick en[/] [bold]{Markup.Escape(canal)}[/][grey].[/]");
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
    storeSesion.Guardar(new SesionTerminal(nick, canal, DateTime.Now));

AnsiConsole.MarkupLine(
    $"[green]●[/] Conectado a [bold]{Markup.Escape(canal)}[/] como [bold]{Markup.Escape(nick)}[/] · " +
    $"{cliente.VistaUsuarios.Count} en la sala · {cliente.EstadisticasActuales.TotalConectados} en total. " +
    "Escribe [grey]/ayuda[/] para ver los comandos.");

// Contadores DESPUÉS de conectar, nada más entrar: total y gente por sala
// (lo mismo que luego lleva el pie fijo de la vista en vivo)
AnsiConsole.MarkupLine(EstadoComoMarkup(cliente));
AnsiConsole.WriteLine();

// ============================================================================
// 💬 ESTADO COMPARTIDO DE LOS BUCLES + Ctrl+C
// ============================================================================

// Dos modos de entrada con el MISMO procesamiento de líneas:
//  - CONSOLA INTERACTIVA → vista en vivo (cabecera y pie fijos con Live)
//  - TUBERÍA redirigida   → consola clásica (pruebas automatizadas: el
//    Smoke no puede leer teclas, así que ahí no se usa el lienzo)
var enVivo = !Console.IsInputRedirected;

var salir = false;

// Estas dos variables se actualizan al cambiar de canal o de nick (las lee
// la cabecera de la vista en vivo y las guarda la salida ordenada)
var nickActual = nick;
var canalActual = canal;

// Salida del último comando (/ayuda, /usuarios...): en tubería se imprime y
// se queda en el scroll; en vivo vive en la zona de mensajes hasta el
// siguiente comando (que la reemplaza) o hasta /limpiar
var salidas = new List<IRenderable>();

// Índice del primer mensaje visible tras /limpiar en la vista en vivo
var mostrarDesde = 0;

Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true; // no matamos el proceso mientras escribe algo

    if (terminal.RecordarSesion)
        storeSesion.Guardar(new SesionTerminal(nickActual, canalActual, DateTime.Now));

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Saliendo... (Ctrl+C)[/]");
    Environment.Exit(0);
};

if (enVivo)
{
    await BucleEnVivo();
}
else
{
    // Modo tubería: Rx entrega la VISTA completa del canal (BehaviorSubject):
    // o se le AÑADEN mensajes (lista anterior + el nuevo) o se REEMPLAZA
    // entera (historial de otro canal). Distinguimos ambos casos para pintar
    // solo lo nuevo: los anteriores siguen al principio por MISMA referencia.
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

    while (!salir)
    {
        // Estado en todo momento: total de conectados y reparto por sala,
        // justo antes de pedir la siguiente línea
        AnsiConsole.MarkupLine(EstadoComoMarkup(cliente));

        var linea = LeerChat();

        // EOF: la tubería de entrada se cerró → salida ordenada como /salir
        if (linea is null)
            break;

        await ProcesarLinea(linea);
    }
}

// ============================================================================
// 🏁 SALIDA ORDENADA
// ============================================================================

if (terminal.RecordarSesion)
    storeSesion.Guardar(new SesionTerminal(nickActual, canalActual, DateTime.Now));

await cliente.CerrarAsync();
AnsiConsole.MarkupLine("[grey]Hasta pronto![/]");
return;


// ============================================================================
// 🧩 Funciones auxiliares (al final, como en el Program del servidor)
// ============================================================================

// ── VISTA EN VIVO (solo consola interactiva) ───────────────────────────────
// AnsiConsole.Live dibuja un lienzo con zonas que se repintan EN SITU: la
// cabecera (identidad + ● de estado) y el pie (contadores + línea de input)
// NO se vuelven a imprimir en el scroll; solo cambian los números que cambian.
// Las teclas se leen SIN bloquear (KeyAvailable + pausa corta) para que el
// repintado siga vivo mientras escribes; las teclas muertas/acentos se
// comportan igual que en los prompts de Spectre (mismo ReadKey por debajo).
async Task BucleEnVivo()
{
    var raiz = new Layout("Raiz")
        .SplitRows(
            new Layout("Cabecera").Size(1),
            new Layout("Mensajes"),
            new Layout("Pie").Size(2));

    // Editor con cursor: la lógica de "dónde estoy escribiendo" vive en
    // EditorLinea (testeable); aquí solo se traducen teclas a sus métodos
    var editor = new EditorLinea();
    var vistaPrevia = cliente.VistaMensajes;

    await AnsiConsole.Live(raiz)
        .StartAsync(async contexto =>
        {
            while (!salir)
            {
                // El historial REEMPLAZADO (cambio de canal) se ve entero: si
                // la lista nueva no continúa a la anterior, se repinta desde 0
                var lista = cliente.VistaMensajes;

                if (!EsContinuacion(lista, vistaPrevia))
                    mostrarDesde = 0;

                vistaPrevia = lista;

                // 1) Cabecera fija: identidad + estado de la conexión
                raiz["Cabecera"].Update(
                    new Markup(CabeceraComoMarkup(cliente, nickActual, canalActual)));

                // 2) Zona de mensajes: lo que toque ver + salida del último comando
                raiz["Mensajes"].Update(CrearZonaMensajes(cliente, mostrarDesde, salidas));

                // 3) Pie fijo: contadores + línea de input con cursor.
                //    Con la conexión caída (reintentos agotados) la fila de
                //    estado se convierte en instrucción: pulsa R para volver
                var filaEstado = cliente.EstadoValor == EstadoConexion.Desconectado
                    ? new Markup(
                        $"{PuntoEstadoComoMarkup(cliente)} [grey]·[/] la reconexión automática " +
                        "se agotó: pulsa [bold]R[/] para reconectar, o [bold]/salir[/] para salir")
                    : new Markup(EstadoComoMarkup(cliente));

                raiz["Pie"].Update(new Rows(
                    filaEstado,
                    new Markup($"[grey]>[/] {editor.ParaPintar()}")));

                contexto.Refresh();

                // Teclas mientras el lienzo está vivo
                while (Console.KeyAvailable)
                {
                    var tecla = Console.ReadKey(intercept: true);

                    if (tecla.Key == ConsoleKey.Enter)
                    {
                        var linea = editor.Texto;
                        editor.Limpiar();
                        await ProcesarLinea(linea); // el bucle repinta el resultado
                    }
                    else if (tecla.Key == ConsoleKey.Backspace)
                    {
                        editor.BorrarAtras();
                    }
                    else if (tecla.Key == ConsoleKey.Delete)
                    {
                        editor.BorrarDelante();
                    }
                    else if (tecla.Key == ConsoleKey.LeftArrow)
                    {
                        editor.MoverIzquierda();
                    }
                    else if (tecla.Key == ConsoleKey.RightArrow)
                    {
                        editor.MoverDerecha();
                    }
                    else if (tecla.Key == ConsoleKey.Home)
                    {
                        editor.Inicio();
                    }
                    else if (tecla.Key == ConsoleKey.End)
                    {
                        editor.Fin();
                    }
                    else if (tecla.Key == ConsoleKey.Escape)
                    {
                        editor.Limpiar(); // Escape = borrar lo escrito
                    }
                    else if (cliente.EstadoValor == EstadoConexion.Desconectado
                             && tecla.Key == ConsoleKey.R
                             && editor.Longitud == 0)
                    {
                        // Solo con la línea vacía: si no, la "r" es texto
                        var errorRecon = await cliente.ReconectarAsync();

                        PublicarLinea(errorRecon is null
                            ? "[green]●[/] Reconectado: sesión recuperada."
                            : $"[red]✗ {Markup.Escape(errorRecon)}[/]");
                    }
                    else if (!char.IsControl(tecla.KeyChar))
                    {
                        editor.Insertar(tecla.KeyChar);
                    }
                }

                await Task.Delay(50);
            }
        });
}

// Procesa una línea escrita por el usuario: un comando o un mensaje. Es el
// MISMO código para los dos modos de entrada; solo cambia DÓNDE acaba la
// salida (scroll de la consola clásica o zona de mensajes del lienzo).
async Task ProcesarLinea(string linea)
{
    switch (ParserComandos.Parse(linea))
    {
        case Comando.Enviar { Texto: var texto } when !string.IsNullOrWhiteSpace(texto):
            error = await cliente.EnviarAsync(texto);

            if (error is not null)
                PublicarLinea($"[red]✗ {Markup.Escape(error)}[/]");
            else if (enVivo)
                salidas.Clear(); // escribir un mensaje deja la salida anterior

            break;

        case Comando.Enviar:
            // Línea vacía o solo espacios: no se envía nada
            break;

        case Comando.Ayuda { Tema: var tema }:
            PublicarContenido(Ayuda.CrearTema(tema));
            break;

        case Comando.Canales:
            PublicarContenido(CrearContenidoCanales(cliente));
            break;

        case Comando.Canal { Nombre: var nombreCanal }:
            error = await CambiarCanalAsync(cliente, chat, nombreCanal);

            if (error is not null)
                PublicarLinea($"[red]✗ {Markup.Escape(error)}[/]");
            else
                canalActual = cliente.Canal ?? canalActual;

            break;

        case Comando.Nick { Nombre: var nombreNick }:
            error = await CambiarNickAsync(cliente, chat, nombreNick);

            if (error is not null)
                PublicarLinea($"[red]✗ {Markup.Escape(error)}[/]");
            else
                nickActual = nombreNick.Trim();

            break;

        case Comando.Usuarios { NombreCanal: var canalConsulta }:
            PublicarContenido(await CrearContenidoUsuariosAsync(cliente, chat, canalConsulta));
            break;

        case Comando.Limpiar:
            AnsiConsole.Clear();
            salidas.Clear();
            mostrarDesde = cliente.VistaMensajes.Count; // solo lo que llegue después
            break;

        case Comando.Salir:
            salir = true;
            break;

        case Comando.Desconocido { Palabra: var palabra }:
            PublicarLinea(
                $"[yellow]Comando no reconocido:[/] {Markup.Escape(palabra)}. " +
                "Escribe [grey]/ayuda[/] para ver todos.");
            break;

        case Comando.Invalido invalido:
            PublicarLineas(
                $"[yellow]{Markup.Escape(invalido.Motivo)}[/]",
                $"[grey]{Markup.Escape(invalido.Uso)}[/]");
            break;
    }
}

// Publica la salida de un comando en el destino del modo actual.
// (Funciones locales SIN sobrecarga: una para líneas y otra para renderables)
void PublicarLineas(params string[] lineas)
{
    if (enVivo)
    {
        salidas.Clear();
        salidas.AddRange(lineas.Select(l => new Markup(l)));
    }
    else
    {
        foreach (var linea in lineas)
            AnsiConsole.MarkupLine(linea);
    }
}

// Variante para renderables de verdad (tablas de /ayuda, listas...)
void PublicarContenido(IRenderable contenido)
{
    if (enVivo)
    {
        salidas.Clear();
        salidas.Add(contenido);
    }
    else
    {
        AnsiConsole.Write(contenido);
        AnsiConsole.WriteLine();
    }
}

void PublicarLinea(string markup) => PublicarLineas(markup);

// Lee una línea con soporte dual: en consola INTERACTIVA, el TextPrompt de
// Spectre (ShowDefaultValue pinta el valor por defecto en verde y Enter lo
// acepta); con la entrada REDIRIGIDA —tubería de pruebas o `echo Ana | ...`—
// Spectre no puede leer teclas (ReadKey no existe sin consola), así que se
// usa Console.ReadLine con el mismo contrato: vacío → defecto, EOF → null.
// defecto null = hay que escribir algo: el vacío lo valida el llamador con
// su propio mensaje en español (no dejamos que Spectre responda en inglés).
static string? PedirTexto(string etiqueta, string? defecto)
{
    if (!Console.IsInputRedirected)
    {
        var prompt = new TextPrompt<string>($"[grey]{Markup.Escape(etiqueta)}[/]");

        if (defecto is not null)
            prompt.DefaultValue(defecto);
        else
            prompt.AllowEmpty();

        return AnsiConsole.Prompt(prompt);
    }

    AnsiConsole.MarkupLine(defecto is not null
        ? $"[grey]{Markup.Escape(etiqueta)}[/] ({Markup.Escape(defecto)})"
        : $"[grey]{Markup.Escape(etiqueta)}[/]:");

    var linea = Console.ReadLine();

    if (linea is null)
        return null; // EOF: no hay más entrada

    return string.IsNullOrWhiteSpace(linea) ? defecto ?? string.Empty : linea.Trim();
}

// Pide el nick en bucle hasta que pase la validación local de ChatConfig.
// Con sesión guardada, la instrucción va DENTRO del prompt (Enter = antiguo);
// sin ella, hay que escribir uno (Enter vacío avisa en español y repite).
// Devuelve null solo si la entrada se cerró (EOF): no hay con qué preguntar.
static string? PedirNick(ChatConfig chat, SesionTerminal? sesion)
{
    var etiqueta = sesion is not null
        ? "Introduce tu nick nuevo o pulsa Enter para usar el antiguo"
        : "Introduce tu nick";

    while (true)
    {
        var nick = PedirTexto(etiqueta, sesion?.Nick);

        if (nick is null)
            return null;

        nick = nick.Trim();
        var error = chat.ValidarUsuario(nick);

        if (error is null)
            return nick;

        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");
    }
}

// Pide el canal en bucle hasta que exista en la configuración, mostrando
// SIEMPRE la lista de canales y cuál se repite con Enter (el último de la
// sesión si existe; si no, el por defecto). Nunca se elige "a ciegas".
// Devuelve null solo si la entrada se cerró (EOF).
static string? PedirCanal(ChatConfig chat, SesionTerminal? sesion)
{
    var ultimoValido = sesion is not null && chat.EsCanalValido(sesion.Canal)
        ? sesion.Canal
        : null;

    // Aviso didáctico: si el canal de la sesión ya no está en la
    // configuración, el programa no se queda sin opciones — cae al default
    if (sesion is not null && ultimoValido is null)
        AnsiConsole.MarkupLine(
            $"[yellow]⚠ Tu último canal ({Markup.Escape(sesion.Canal)}) ya no existe.[/]");

    var defecto = ultimoValido ?? chat.CanalPorDefecto;
    var origen = ultimoValido is not null ? "(tu último canal visitado)" : "(el canal por defecto)";

    AnsiConsole.MarkupLine(
        $"[grey]Elige dónde entrar. Enter =[/] [bold]{Markup.Escape(defecto)}[/] [grey]{origen}[/]");

    // Lista completa de canales, con ← marcando el que se repetirá con Enter
    var hex = TemaDiscord.Blurple.ToMarkup();
    var lista = string.Join("   ", chat.Canales.Select(c =>
        c.Equals(defecto, StringComparison.OrdinalIgnoreCase)
            ? $"[{hex}]{Markup.Escape(c)}[/] ←"
            : Markup.Escape(c)));

    AnsiConsole.MarkupLine($"[grey]  {lista}[/]");

    while (true)
    {
        var escrito = PedirTexto("Canal", defecto);

        if (escrito is null)
            return null;

        var canonico = chat.EncontrarCanal(escrito);

        if (canonico is not null)
            return canonico;

        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(chat.ErrorCanalDesconocido(escrito.Trim()))}[/]");
    }
}

// Lee una línea del chat en MODO TUBERÍA (sin lienzo, para automatización).
// Devuelve null SOLO con EOF: el bucle lo usa para salir ordenadamente,
// igual que si el usuario escribiese /salir.
static string? LeerChat()
{
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

// Consulta GET /estadisticas ANTES de registrarse: solo para decir cuánta
// gente hay. Un fallo de red o de JSON no corta el arranque (luego ya se
// verá al conectar); lo único que se pierde es la cifra de bienvenida.
static async Task PintarConectadosInicialesAsync(string urlServidor, ChatConfig chat)
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        var json = await http.GetStringAsync(
            $"{urlServidor.TrimEnd('/')}{ChatRutas.Estadisticas}");

        // El servidor serializa en camelCase (default de los minimal APIs);
        // con CaseInsensitive llega a los parámetros del record sin más
        var est = JsonSerializer.Deserialize<EstadisticasDto>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (est is not null)
            AnsiConsole.MarkupLine(Conectados.DescripcionArranque(chat, est));
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
    {
        AnsiConsole.MarkupLine(
            "[yellow]⚠ No pude consultar cuántos hay conectados (¿está el servidor arrancado?)[/]");
    }
}

// El ● de Discord (compartido por la cabecera en vivo y la línea de estado):
// verde conectado, gris conectando, naranja reconectando, rojo caído.
static string PuntoEstadoComoMarkup(ClienteChat cliente)
{
    var (color, texto) = cliente.EstadoValor switch
    {
        EstadoConexion.Conectado => ("green", "conectado"),
        EstadoConexion.Conectando => ("grey", "conectando"),
        EstadoConexion.Reconectando => ("orange", "reconectando"),
        _ => ("red", "desconectado")
    };

    return $"[{color}]● {texto}[/]";
}

// Cabecera de la vista en vivo: identidad + punto de estado de la conexión
static string CabeceraComoMarkup(ClienteChat cliente, string nick, string canal)
{
    // ESTILO COMBINADO en una sola etiqueta ([#hex bold], como [bold red]):
    // nunca un ToMarkup() con ] huérfano — eso rompía el primer repintado
    return $"[{TemaDiscord.Blurple.ToMarkup()} bold]AulaChatNet[/] [grey]·[/] " +
           $"{Markup.Escape(nick)} [grey]·[/] {Markup.Escape(canal)} [grey]·[/] " +
           PuntoEstadoComoMarkup(cliente);
}

// Línea de estado (pie fijo en vivo; línea previa al prompt en tubería):
// punto de conexión + total de conectados y reparto por SALA, en orden de la
// configuración. El ● marca el canal en el que estamos; las salas vacías
// muestran 0 (el servidor solo emite las que tienen gente). Las fórmulas
// viven en Conectados para poder testearlas sin consola.
static string EstadoComoMarkup(ClienteChat cliente)
{
    var est = cliente.EstadisticasActuales;

    return PuntoEstadoComoMarkup(cliente) + " [grey]·[/] " +
           $"[grey]📌 {est.TotalConectados} conectados:[/] " +
           Conectados.ConteosPorSala(cliente.Canales, est, cliente.Canal);
}

// Una línea de mensaje como markup (la MISMA fórmula en los dos modos):
// sistema en gris cursiva; normal con hora, iniciales entre corchetes y
// nombre con su color derivado. El servidor marca en UTC (la regla de oro:
// guardar UTC, convertir SOLO al presentar), así que aquí se pasa a la
// hora local del que mira — sin esto, la terminal enseñaría otra hora.
static string LineaMensajeMarkup(MensajeDto mensaje)
{
    if (mensaje.Tipo == TipoMensaje.Sistema)
        return $"[grey italic]{Markup.Escape(mensaje.Texto)}[/]";

    var hex = TemaDiscord.ColorDe(mensaje.Usuario).ToMarkup();
    var iniciales = TemaDiscord.Iniciales(mensaje.Usuario);

    return $"[grey]{mensaje.Timestamp.ToLocalTime():HH:mm}[/] " +
           $"[{hex}][[{iniciales}]][/] " +
           $"[{hex}]{Markup.Escape(mensaje.Usuario)}[/]: {Markup.Escape(mensaje.Texto)}";
}

// Contenido de la zona de mensajes de la vista en vivo: últimas líneas
// visibles + la salida del último comando, si la hay.
static IRenderable CrearZonaMensajes(ClienteChat cliente, int mostrarDesde, List<IRenderable> salidas)
{
    var lista = cliente.VistaMensajes;

    // La zona recorta por abajo y queremos ver SIEMPRE lo último: nos
    // quedamos con las últimas N líneas del alto disponible (margen por si
    // un mensaje largo ocupa dos filas al envolverse)
    var alto = Math.Max(6, AnsiConsole.Console.Profile.Height - 8);
    var desde = Math.Clamp(Math.Max(mostrarDesde, lista.Count - alto), 0, lista.Count);

    var filas = lista
        .Skip(desde)
        .Select(m => (IRenderable)new Markup(LineaMensajeMarkup(m)))
        .ToList();

    if (salidas.Count > 0)
    {
        filas.Add(new Markup(string.Empty));
        filas.AddRange(salidas);
    }

    return filas.Count > 0 ? new Rows(filas.ToArray()) : new Markup(string.Empty);
}

// /canales: lista de canales con sus conteos, marcando el actual.
static IRenderable CrearContenidoCanales(ClienteChat cliente)
{
    var est = cliente.EstadisticasActuales;

    var porCanal = est.Canales
        .ToDictionary(c => c.Canal, c => c.Conectados, StringComparer.OrdinalIgnoreCase);

    var filas = new List<Markup>
    {
        new($"[grey]📌 {est.TotalConectados} conectados en total:[/]")
    };

    foreach (var canalDisponible in cliente.Canales)
    {
        porCanal.TryGetValue(canalDisponible, out var conectados);
        var esActual = string.Equals(canalDisponible, cliente.Canal, StringComparison.OrdinalIgnoreCase);
        var marca = esActual ? " [green]● actual[/]" : "";

        filas.Add(new Markup(
            $"  [{TemaDiscord.Blurple.ToMarkup()}]{Markup.Escape(canalDisponible)}[/] " +
            $"[white]{conectados}[/]{marca}"));
    }

    return new Rows(filas.ToArray());
}

// /usuarios [canal]: sin argumento, los de TU canal con la lista local
// (mismo dato, sin red); con argumento, se consulta el servidor para mirar
// dentro de otra sala sin cambiar de canal.
static async Task<IRenderable> CrearContenidoUsuariosAsync(
    ClienteChat cliente, ChatConfig chat, string? canalConsulta)
{
    if (string.IsNullOrWhiteSpace(canalConsulta))
        return CrearFilasUsuarios(cliente.Canal ?? string.Empty, cliente.VistaUsuarios);

    var canonico = chat.EncontrarCanal(canalConsulta);

    if (canonico is null)
        return new Markup($"[red]✗ {Markup.Escape(chat.ErrorCanalDesconocido(canalConsulta.Trim()))}[/]");

    if (string.Equals(canonico, cliente.Canal, StringComparison.OrdinalIgnoreCase))
        return CrearFilasUsuarios(canonico, cliente.VistaUsuarios);

    var usuarios = await cliente.ObtenerUsuariosAsync(canonico);

    // null = canal desconocido O no se pudo consultar (conexión caída):
    // el mismo mensaje sirve para los dos casos sin mentir en ninguno
    return usuarios is null
        ? new Markup("[red]✗ No pude consultar la lista de ese canal.[/]")
        : CrearFilasUsuarios(canonico, usuarios);
}

// Lista de usuarios con su cuenta (compartida por /usuarios y /usuarios #canal)
static IRenderable CrearFilasUsuarios(string canal, IReadOnlyList<UsuarioConectadoDto> usuarios)
{
    var filas = new List<Markup>
    {
        new($"[grey]📌 {usuarios.Count} en línea en[/] [bold]{Markup.Escape(canal)}[/]:")
    };

    if (usuarios.Count == 0)
    {
        filas.Add(new Markup("[grey]Aún no hay nadie conectado en este canal.[/]"));
        return new Rows(filas.ToArray());
    }

    foreach (var usuario in usuarios)
    {
        var hex = TemaDiscord.ColorDe(usuario.Usuario).ToMarkup();

        filas.Add(new Markup($"[green]●[/] [{hex}]{Markup.Escape(usuario.Usuario)}[/]"));
    }

    return new Rows(filas.ToArray());
}

// Cambia de canal validando ANTES en local (misma ChatConfig que el servidor).
// Devuelve null si se hizo; el error, si no (quien lo pinta es ProcesarLinea).
static async Task<string?> CambiarCanalAsync(ClienteChat cliente, ChatConfig chat, string nombreCanal)
{
    if (!chat.EsCanalValido(nombreCanal))
        return chat.ErrorCanalDesconocido(nombreCanal);

    return await cliente.CambiarCanalAsync(nombreCanal);
}

// Cambia el nick validando ANTES en local; el aviso "X ahora se llama Y"
// lo pinta el servidor por el flujo normal de mensajes. Devuelve null si se
// hizo; el error, si no.
static async Task<string?> CambiarNickAsync(ClienteChat cliente, ChatConfig chat, string nombreNick)
{
    var error = chat.ValidarUsuario(nombreNick.Trim());

    return error is not null ? error : await cliente.CambiarNickAsync(nombreNick.Trim());
}

// Pinta un mensaje recibido en el modo MODO TUBERÍA (el en vivo construye
// las filas con LineaMensajeMarkup y repinta la zona entera).
static void PintarMensaje(MensajeDto mensaje) =>
    AnsiConsole.MarkupLine(LineaMensajeMarkup(mensaje));
