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

        // 1) Historial del canal SOLO a este cliente: no se satura a los demás
        await Clients.Caller.SendAsync(ChatHubEventos.HistorialRecibido, store.GetHistorial(canalCanonico));

        // 2) "X se ha unido al canal" a los miembros del canal (también al que entra)
        var mensaje = MensajesSistema.UsuarioUnido(usuario);
        store.AddMensaje(canalCanonico, mensaje);
        await Clients.Group(canalCanonico).SendAsync(ChatHubEventos.MensajeNuevo, mensaje);

        // 3) Lista de usuarios del canal actualizada (punto verde de en línea)
        await Clients.Group(canalCanonico).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(canalCanonico));

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

        // Aviso de SALIDA en el canal anterior (para los que se quedan allí)
        var salida = MensajesSistema.UsuarioSalio(conexion.Usuario);
        store.AddMensaje(conexion.Canal, salida);
        await Clients.Group(conexion.Canal).SendAsync(ChatHubEventos.MensajeNuevo, salida);
        await Clients.Group(conexion.Canal).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(conexion.Canal));

        // Historial del nuevo canal SOLO al que cambia (antes del aviso de
        // entrada, para que no se pinte dos veces la unión)
        await Clients.Caller.SendAsync(ChatHubEventos.HistorialRecibido, store.GetHistorial(canalNuevo));

        // Aviso de ENTRADA en el nuevo canal
        var entrada = MensajesSistema.UsuarioUnido(conexion.Usuario);
        store.AddMensaje(canalNuevo, entrada);
        await Clients.Group(canalNuevo).SendAsync(ChatHubEventos.MensajeNuevo, entrada);
        await Clients.Group(canalNuevo).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(canalNuevo));

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
    /// Desconexión: si el usuario ya estaba registrado, se despide en su
    /// nombre dentro de SU canal y se actualiza su lista de conectados.
    /// </summary>
    /// <param name="exception">Excepción que causó la caída, si la hubo.</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
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
        }
        else
        {
            logger.LogInformation(
                "Conexión sin registrar cerrada: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
