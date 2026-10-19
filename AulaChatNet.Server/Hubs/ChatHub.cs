using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Hubs;

/// <summary>
/// Hub de SignalR del chat del aula: transporta los mensajes en tiempo real
/// entre los clientes (terminal y Blazor) y el store en memoria.
///
/// <remarks>
/// Cada canal de la configuración es un GRUPO de SignalR: los mensajes se
/// emiten con <c>Clients.Group(canal)</c>, así cada sala recibe solo lo suyo.
///
/// Flujo de la Fase 1:
/// <list type="number">
/// <item>El cliente se conecta al hub y llama a <see cref="ConnectAsync"/>
/// con su nombre (y opcionalmente su canal; si no, el por defecto).</item>
/// <item>El servidor valida, registra y le envía SOLO a él el historial del
/// canal (<see cref="ChatHubEventos.HistorialRecibido"/>).</item>
/// <item>Cada <see cref="SendAsync"/> se difunde al grupo de su canal.</item>
/// <item><see cref="CambiarCanalAsync"/> mueve al usuario entre grupos
/// (aviso de salida en el viejo, de entrada en el nuevo).</item>
/// <item>Al desconectar se emite "X ha salido del canal" al SUYO.</item>
/// </list>
/// El nombre llega como parámetro de ConnectAsync (y no en OnConnectedAsync)
/// porque SignalR conecta primero y "quién eres" se dice después: así el
/// servidor puede rechazar nombres inválidos o duplicados devolviendo un
/// mensaje de error al cliente.
///
/// Matiz conocido: en el instante exacto de un cambio de canal puede
/// colarse un mensaje de la sala anterior (carrera inherente a los grupos);
/// el cliente, al recibir el historial del nuevo canal, repinta y lo corrige.
/// </remarks>
/// </summary>
/// <example>
/// ws://localhost:5030/hubs/chat
/// <code>
/// await conexion.InvokeAsync(ChatHubMetodos.Conectar, "Ana", "#smr"); // → null si OK
/// await conexion.InvokeAsync(ChatHubMetodos.CambiarCanal, "#daw");    // → null si OK
/// await conexion.InvokeAsync(ChatHubMetodos.Enviar, "Buenas");        // → null si OK
/// </code>
/// </example>
public class ChatHub(IChatStore store, IOptions<ChatConfig> opciones, ILogger<ChatHub> logger) : Hub
{
    private ChatConfig Config => opciones.Value;

    /// <summary>
    /// Conexión entrante: aún no hay nombre; el cliente se registrará
    /// después con <see cref="ConnectAsync"/>.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        logger.LogInformation(
            "Conexión nueva pendiente de registro: {ConnectionId}", Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Da de alta al usuario en un canal y le envía el historial de ese canal.
    /// </summary>
    /// <param name="usuario">Nombre elegido (se recorta y se valida con ChatConfig).</param>
    /// <param name="canal">Canal a entrar (opcional; si se omite, el por defecto de la config).</param>
    /// <returns>null si todo fue correcto; el mensaje de error si no (el cliente se desconecta).</returns>
    public async Task<string?> ConnectAsync(string? usuario, string? canal = null)
    {
        usuario = usuario?.Trim() ?? string.Empty;

        // Si no indican canal, se entra al primero de la configuración
        var canalCanonico = Config.EncontrarCanal(
            string.IsNullOrWhiteSpace(canal) ? Config.CanalPorDefecto : canal);

        if (canalCanonico is null)
        {
            var errorCanal = Config.ErrorCanalDesconocido(canal?.Trim() ?? string.Empty);
            logger.LogWarning(
                "Registro rechazado ({ConnectionId}): {Error}", Context.ConnectionId, errorCanal);

            return errorCanal;
        }

        // Una sola expresión: valida el nombre Y lo registra en el canal
        var error = Config.ValidarUsuario(usuario)
                    ?? store.AddUsuario(canalCanonico, usuario, Context.ConnectionId);

        if (error is not null)
        {
            logger.LogWarning(
                "Registro rechazado ({ConnectionId}): {Error}", Context.ConnectionId, error);

            return error;
        }

        logger.LogInformation(
            "Usuario {Usuario} conectado al canal {Canal} ({ConnectionId})",
            usuario, canalCanonico, Context.ConnectionId);

        // 0) Alta en el GRUPO de SignalR del canal: cada canal es un grupo y
        // sin este paso los envíos a Clients.Group(canal) irían a un grupo
        // vacío y no los recibiría nadie (el historial, que va a Clients.Caller,
        // sí llegaría: por eso el fallo es tan silencioso)
        await Groups.AddToGroupAsync(Context.ConnectionId, canalCanonico);

        // 1) Historial del canal SOLO a este cliente: no se satura a los demás
        await Clients.Caller.SendAsync(ChatHubEventos.HistorialRecibido, store.GetHistorial(canalCanonico));

        // 1b) Reacciones de ese historial SOLO a este cliente: quien llega
        //     tarde también ve los chips de los mensajes que siguen vivos
        await Clients.Caller.SendAsync(ChatHubEventos.ReaccionesRecibidas, store.GetReacciones(canalCanonico));

        // 2) "X se ha unido al canal" a los miembros del canal (también al que entra)
        var mensaje = MensajesSistema.UsuarioUnido(usuario);
        store.AddMensaje(canalCanonico, mensaje);
        await Clients.Group(canalCanonico).SendAsync(ChatHubEventos.MensajeNuevo, mensaje);

        // 3) Lista de usuarios del canal actualizada (punto verde de en línea)
        await Clients.Group(canalCanonico).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(canalCanonico));

        // 4) Estadísticas globales a TODOS: contadores de total y por sala
        //    para la línea de estado de cada cliente
        await Clients.All.SendAsync(ChatHubEventos.EstadisticasActualizadas, store.GetEstadisticas());

        return null;
    }

    /// <summary>
    /// Mueve al usuario registrado a otro canal, con los avisos propios de
    /// salida (en el canal viejo) y entrada (en el nuevo).
    /// </summary>
    /// <param name="canal">Canal de destino (se valida contra la configuración).</param>
    /// <returns>null si el cambio fue correcto; el mensaje de error si no.</returns>
    public async Task<string?> CambiarCanalAsync(string? canal)
    {
        var conexion = store.GetConexion(Context.ConnectionId);

        if (conexion is null)
            return "No estás registrado en el canal, vuelve a conectarte";

        var canalNuevo = Config.EncontrarCanal(canal);

        if (canalNuevo is null)
        {
            var errorCanal = Config.ErrorCanalDesconocido(canal?.Trim() ?? string.Empty);
            logger.LogWarning(
                "Cambio de canal rechazado ({ConnectionId}): {Error}", Context.ConnectionId, errorCanal);

            return errorCanal;
        }

        // Ya está en ese canal: no se emite nada (el cliente solo repinta)
        if (string.Equals(canalNuevo, conexion.Canal, StringComparison.OrdinalIgnoreCase))
            return null;

        var error = store.CambiarCanal(Context.ConnectionId, canalNuevo);

        if (error is not null)
        {
            logger.LogWarning(
                "Cambio de canal rechazado de {Usuario} ({ConnectionId}): {Error}",
                conexion.Usuario, Context.ConnectionId, error);

            return error;
        }

        logger.LogInformation(
            "Usuario {Usuario} cambia de {CanalViejo} a {CanalNuevo}",
            conexion.Usuario, conexion.Canal, canalNuevo);

        // Intercambio de grupos: fuera del canal viejo ANTES de los avisos (el
        // aviso de salida es para los que se quedan, no para el que se marcha)
        // y dentro del nuevo para recibir los mensajes del suyo
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, conexion.Canal);
        await Groups.AddToGroupAsync(Context.ConnectionId, canalNuevo);

        // Aviso de SALIDA en el canal anterior (para los que se quedan allí)
        var salida = MensajesSistema.UsuarioSalio(conexion.Usuario);
        store.AddMensaje(conexion.Canal, salida);
        await Clients.Group(conexion.Canal).SendAsync(ChatHubEventos.MensajeNuevo, salida);
        await Clients.Group(conexion.Canal).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(conexion.Canal));

        // Historial del nuevo canal SOLO al que cambia (antes del aviso de
        // entrada, para que no se pinte dos veces la unión)
        await Clients.Caller.SendAsync(ChatHubEventos.HistorialRecibido, store.GetHistorial(canalNuevo));

        // Reacciones del nuevo canal SOLO al que cambia: los chips de los
        // mensajes que trae su historial (mismo orden: tras el historial)
        await Clients.Caller.SendAsync(ChatHubEventos.ReaccionesRecibidas, store.GetReacciones(canalNuevo));

        // Aviso de ENTRADA en el nuevo canal
        var entrada = MensajesSistema.UsuarioUnido(conexion.Usuario);
        store.AddMensaje(canalNuevo, entrada);
        await Clients.Group(canalNuevo).SendAsync(ChatHubEventos.MensajeNuevo, entrada);
        await Clients.Group(canalNuevo).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(canalNuevo));

        // Estadísticas globales a TODOS: los contadores por sala han cambiado
        await Clients.All.SendAsync(ChatHubEventos.EstadisticasActualizadas, store.GetEstadisticas());

        return null;
    }

    /// <summary>
    /// Cambia el nombre del usuario registrado dentro de su canal actual
    /// (como "Cambiar apodo" de Discord), con el aviso correspondiente.
    /// </summary>
    /// <param name="nuevoNick">Nuevo nombre (se recorta y se valida con ChatConfig).</param>
    /// <returns>null si el cambio fue correcto; el mensaje de error si no.</returns>
    public async Task<string?> CambiarNickAsync(string? nuevoNick)
    {
        var conexion = store.GetConexion(Context.ConnectionId);

        if (conexion is null)
            return "No estás registrado en el canal, vuelve a conectarte";

        nuevoNick = nuevoNick?.Trim() ?? string.Empty;

        var error = Config.ValidarUsuario(nuevoNick);

        if (error is not null)
        {
            logger.LogWarning(
                "Cambio de nick rechazado ({ConnectionId}): {Error}", Context.ConnectionId, error);

            return error;
        }

        // Mismo nombre (ignorando mayúsculas): no se emite nada
        if (string.Equals(conexion.Usuario, nuevoNick, StringComparison.OrdinalIgnoreCase))
            return null;

        error = store.CambiarNick(Context.ConnectionId, nuevoNick);

        if (error is not null)
        {
            logger.LogWarning(
                "Cambio de nick rechazado de {Usuario} ({ConnectionId}): {Error}",
                conexion.Usuario, Context.ConnectionId, error);

            return error;
        }

        logger.LogInformation(
            "Usuario {UsuarioAntiguo} ahora se llama {UsuarioNuevo} ({ConnectionId})",
            conexion.Usuario, nuevoNick, Context.ConnectionId);

        // "X ahora se llama Y" a los miembros del canal (también al propio autor)
        var mensaje = MensajesSistema.NickCambiado(conexion.Usuario, nuevoNick);
        store.AddMensaje(conexion.Canal, mensaje);
        await Clients.Group(conexion.Canal).SendAsync(ChatHubEventos.MensajeNuevo, mensaje);

        // Lista actualizada: el nick cambia en el panel de conectados
        await Clients.Group(conexion.Canal).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(conexion.Canal));

        return null;
    }

    /// <summary>
    /// Devuelve los usuarios de CUALQUIER canal (consulta bajo demanda):
    /// solo responde AL QUE PREGUNTA y no difunde nada a nadie más, por lo
    /// que sirve para mirar dentro de otra sala sin cambiar de canal.
    /// </summary>
    /// <param name="canal">Canal a consultar (se valida con la configuración).</param>
    /// <returns>La lista de conectados del canal, o null si el canal no existe.</returns>
    public Task<IReadOnlyList<UsuarioConectadoDto>?> ObtenerUsuariosAsync(string? canal)
    {
        var canonico = Config.EncontrarCanal(canal);

        if (canonico is null)
        {
            logger.LogWarning(
                "Consulta de usuarios rechazada ({ConnectionId}): canal {Canal}", Context.ConnectionId, canal);
            return Task.FromResult<IReadOnlyList<UsuarioConectadoDto>?>(null);
        }

        return Task.FromResult<IReadOnlyList<UsuarioConectadoDto>?>(store.GetUsuarios(canonico));
    }

    /// <summary>
    /// Difunde un mensaje del usuario registrado al grupo de SU canal.
    /// </summary>
    /// <param name="texto">Texto escrito (se valida con ChatConfig).</param>
    /// <returns>null si todo fue correcto; el mensaje de error si no.</returns>
    public async Task<string?> SendAsync(string? texto)
    {
        var conexion = store.GetConexion(Context.ConnectionId);

        if (conexion is null)
        {
            logger.LogWarning(
                "Mensaje rechazado: la conexión {ConnectionId} no está registrada",
                Context.ConnectionId);

            return "No estás registrado en el canal, vuelve a conectarte";
        }

        texto = texto?.Trim() ?? string.Empty;

        var error = Config.ValidarMensaje(texto);

        if (error is not null)
        {
            logger.LogWarning("Mensaje rechazado de {Usuario}: {Error}", conexion.Usuario, error);

            return error;
        }

        var mensaje = new MensajeDto(
            Guid.NewGuid(), conexion.Usuario, texto, DateTime.UtcNow, TipoMensaje.Normal);

        store.AddMensaje(conexion.Canal, mensaje);

        // Se loguea la longitud, no el contenido: costumbre de buenas prácticas
        // (los logs pueden verse personas no autorizadas)
        logger.LogInformation(
            "{Usuario} envía mensaje de {Longitud} caracteres a {Canal}",
            conexion.Usuario, texto.Length, conexion.Canal);

        await Clients.Group(conexion.Canal).SendAsync(ChatHubEventos.MensajeNuevo, mensaje);

        return null;
    }

    /// <summary>
    /// Avisa de que este usuario está escribiendo en SU canal. El QUINTO
    /// destino de SignalR del proyecto: <c>Clients.OthersInGroup</c>, todos
    /// los miembros de la sala MENOS el que escribe (para él no es noticia
    /// saber que se está escribiendo a sí mismo).
    ///
    /// <remarks>
    /// Tres decisiones que hacen de esto un aviso inocuo:
    /// <list type="bullet">
    /// <item><b>Sin estado en el servidor</b>: no se guarda quién escribe ni
    /// cuándo. Cada cliente caduca sus propias marcas a los cinco segundos,
    /// así que nadie tiene nada que limpiar al desconectar.</item>
    /// <item><b>Sin <c>AddMensaje</c></b>: la señal no entra en el historial
    /// ni en la cola de escritura de SQLite — una pulsación de teclas no es
    /// un mensaje.</item>
    /// <item><b>Sin texto</b>: solo viaja el nombre de quien escribe, nunca
    /// lo que está escribiendo.</item>
    /// </list>
    /// </remarks>
    /// </summary>
    /// <returns>null si estaba registrado; el mensaje de error si no.</returns>
    public async Task<string?> EscribiendoAsync()
    {
        var conexion = store.GetConexion(Context.ConnectionId);

        if (conexion is null)
        {
            logger.LogWarning(
                "Señal de escritura rechazada: la conexión {ConnectionId} no está registrada",
                Context.ConnectionId);

            return "No estás registrado en el canal, vuelve a conectarte";
        }

        // Log a nivel Debug a propósito: una señal por segundo y persona no
        // debe llenar el fichero de log (el nivel por defecto es Information)
        logger.LogDebug(
            "Señal de escritura de {Usuario} en {Canal}", conexion.Usuario, conexion.Canal);

        await Clients.OthersInGroup(conexion.Canal).SendAsync(
            ChatHubEventos.Escribiendo, conexion.Usuario);

        return null;
    }

    /// <summary>
    /// Alterna una reacción sobre un mensaje del canal del usuario: si ya la
    /// tenía, se la quita; si no, se la pone. Difunde el estado COMPLETO del
    /// mensaje a <c>Clients.Group</c> — el emisor incluido, porque él también
    /// debe ver su propio chip (a diferencia de la señal de escritura, aquí
    /// la noticia es para todos).
    ///
    /// <remarks>
    /// Cuatro reglas que sostienen este método y que conviene leer juntas:
    /// <list type="number">
    /// <item>El emoji se valida contra la lista CERRADA de
    /// <see cref="EmojisReaccion"/>: de la red llega lo que quiera, y sin
    /// esa puerta cualquier texto libre ensuciaría el canal para todos.</item>
    /// <item>Solo se reacciona a mensajes NORMALES que sigan en la caché del
    /// canal del emisor: los avisos de sistema no lo son, y lo que salió de
    /// la caché circular ya no se puede reaccionar.</item>
    /// <item><b>No se llama a <c>AddMensaje</c> ni se persiste nada</b>: una
    /// reacción no es un mensaje y no debe entrar en el historial de SQLite.
    /// Sin autenticación, el nombre que dejaste libre al reiniciar lo puede
    /// coger cualquiera y se llevaría tus reacciones: efímero, como los
    /// privados.</item>
    /// <item>El servidor es la ÚNICA fuente de verdad: el cliente solo
    /// invoca, y el estado que pinta viene del evento, nunca de un cálculo
    /// propio — así no hay dos pantallas que puedan discrepar.</item>
    /// </list>
    /// </remarks>
    /// </summary>
    /// <param name="idMensaje">Identificador del mensaje a reaccionar.</param>
    /// <param name="emoji">Emoji escrito por el cliente (se canoniciza y se valida aquí).</param>
    /// <returns>null si el alterna fue correcto; el mensaje de error si no.</returns>
    public async Task<string?> ReaccionarAsync(Guid idMensaje, string? emoji)
    {
        var conexion = store.GetConexion(Context.ConnectionId);

        if (conexion is null)
        {
            logger.LogWarning(
                "Reacción rechazada: la conexión {ConnectionId} no está registrada",
                Context.ConnectionId);

            return "No estás registrado en el canal, vuelve a conectarte";
        }

        var canonico = EmojisReaccion.Canonico(emoji);

        if (canonico is null)
        {
            logger.LogWarning(
                "Reacción rechazada de {Usuario}: emoji no admitido", conexion.Usuario);

            return $"Emoji no admitido. Usa: {EmojisReaccion.ListaLegible}";
        }

        var estado = store.AlternarReaccion(conexion.Canal, idMensaje, canonico, conexion.Usuario);

        if (estado is null)
        {
            logger.LogWarning(
                "Reacción rechazada de {Usuario}: el mensaje {IdMensaje} no está en {Canal}",
                conexion.Usuario, idMensaje, conexion.Canal);

            return "Ese mensaje ya no está en el canal";
        }

        logger.LogInformation(
            "{Usuario} reacciona {Emoji} al mensaje {IdMensaje} en {Canal}",
            conexion.Usuario, canonico, idMensaje, conexion.Canal);

        await Clients.Group(conexion.Canal).SendAsync(ChatHubEventos.ReaccionActualizada, estado);

        return null;
    }

    /// <summary>
    /// Envía un mensaje a UNA persona concreta del canal del emisor, sin que
    /// lo vea nadie más. Es el CUARTO destino de SignalR del proyecto:
    /// <c>Group</c> (todo el canal), <c>Caller</c> (solo el que pregunta) y
    /// <c>All</c> (todos) ya estaban; este va a dos conexiones concretas.
    ///
    /// <remarks>
    /// Cuatro reglas que sostienen la privacidad de este método, y que
    /// conviene leer juntas porque son las que lo hacen seguro:
    /// <list type="number">
    /// <item>El destinatario se busca <b>dentro del canal del emisor</b>: los
    /// nombres solo son únicos en una sala, así que sin ese filtro dos
    /// personas distintas podrían llamarse igual en salas distintas.</item>
    /// <item><b>No se llama a <c>AddMensaje</c></b>: el privado no entra en el
    /// historial de canal ni en su cola de escritura, de modo que quien entre
    /// después no se lo puede encontrar.</item>
    /// <item><b>No se persiste</b>: sin autenticación, el nombre que dejaste
    /// libre al reiniciar lo puede coger cualquiera y se llevaría tus
    /// mensajes privados. Efímero es la única opción honesta.</item>
    /// <item>El eco al autor sale del MISMO servidor: las dos partes reciben
    /// el mismo objeto en el mismo orden, sin montar la vista a mano.</item>
    /// </list>
    /// </remarks>
    /// </summary>
    /// <param name="destinatario">Nombre de quien lo recibe (en el canal del emisor).</param>
    /// <param name="texto">Texto escrito (misma validación que un mensaje normal).</param>
    /// <returns>null si todo fue correcto; el mensaje de error si no.</returns>
    public async Task<string?> EnviarPrivadoAsync(string? destinatario, string? texto)
    {
        var conexion = store.GetConexion(Context.ConnectionId);

        if (conexion is null)
        {
            logger.LogWarning(
                "Privado rechazado: la conexión {ConnectionId} no está registrada",
                Context.ConnectionId);

            return "No estás registrado en el canal, vuelve a conectarte";
        }

        destinatario = destinatario?.Trim() ?? string.Empty;

        if (destinatario.Length == 0)
            return Config.ErrorPrivadoDestinoVacio;

        // Mismo nombre solo en mayúsculas: es la misma persona (el store
        // compara igual cuando da de alta, así que las dos reglas cuadran)
        if (string.Equals(conexion.Usuario, destinatario, StringComparison.OrdinalIgnoreCase))
            return Config.ErrorPrivadoAUnoMismo;

        texto = texto?.Trim() ?? string.Empty;

        var error = Config.ValidarMensaje(texto);

        if (error is not null)
        {
            logger.LogWarning(
                "Privado rechazado de {Usuario}: {Error}", conexion.Usuario, error);

            return error;
        }

        // El canal del emisor decide a quién se puede hablar: nadie de otra
        // sala (y null significa "no hay nadie con ese nombre AQUÍ")
        var connectionIdDestino = store.GetConnectionId(conexion.Canal, destinatario);

        if (connectionIdDestino is null)
        {
            logger.LogWarning(
                "Privado rechazado de {Usuario} a {Destinatario}: sin destino en {Canal}",
                conexion.Usuario, destinatario, conexion.Canal);

            return Config.ErrorPrivadoSinDestino(destinatario);
        }

        var mensaje = new MensajePrivadoDto(
            Guid.NewGuid(), conexion.Usuario, destinatario, texto, DateTime.UtcNow);

        // Se loguea la longitud, no el contenido (mismo criterio que SendAsync:
        // los logs pueden verse personas no autorizadas)
        logger.LogInformation(
            "{Usuario} envía un privado de {Longitud} caracteres a {Destinatario} ({Canal})",
            conexion.Usuario, texto.Length, destinatario, conexion.Canal);

        // Primero al destinatario y después el eco al autor (Clients.Caller):
        // un único emisor, dos destinos concretos, jamás Clients.Group
        await Clients.Client(connectionIdDestino).SendAsync(ChatHubEventos.PrivadoRecibido, mensaje);
        await Clients.Caller.SendAsync(ChatHubEventos.PrivadoRecibido, mensaje);

        return null;
    }

    /// <summary>
    /// Desconexión: si el usuario ya estaba registrado, se despide en su
    /// nombre dentro de SU canal y se actualiza su lista de conectados.
    /// </summary>
    /// <param name="exception">Excepción que causó la caída, si la hubo.</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR da de baja esta conexión de todos sus grupos al cerrarse,
        // así que no hace falta quitarla del canal a mano
        var conexion = store.RemoveUsuario(Context.ConnectionId);

        if (conexion is not null)
        {
            if (exception is not null)
                logger.LogWarning(exception,
                    "Usuario {Usuario} se desconectó con error ({ConnectionId})",
                    conexion.Usuario, Context.ConnectionId);
            else
                logger.LogInformation(
                    "Usuario {Usuario} desconectado de {Canal} ({ConnectionId})",
                    conexion.Usuario, conexion.Canal, Context.ConnectionId);

            var mensaje = MensajesSistema.UsuarioSalio(conexion.Usuario);
            store.AddMensaje(conexion.Canal, mensaje);

            // El aviso es solo para los miembros de ese canal
            await Clients.Group(conexion.Canal).SendAsync(ChatHubEventos.MensajeNuevo, mensaje);
            await Clients.Group(conexion.Canal).SendAsync(
                ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(conexion.Canal));

            // Estadísticas globales a TODOS: uno menos conectado
            await Clients.All.SendAsync(ChatHubEventos.EstadisticasActualizadas, store.GetEstadisticas());
        }
        else
        {
            logger.LogInformation(
                "Conexión sin registrar cerrada: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
