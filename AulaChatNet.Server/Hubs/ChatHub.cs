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
/// Flujo de la Fase 1:
/// <list type="number">
/// <item>El cliente se conecta al hub y llama a <see cref="ConnectAsync"/>
/// con su nombre de usuario.</item>
/// <item>El servidor valida, registra y le envía SOLO a él el historial
/// (<see cref="ChatHubEventos.HistorialRecibido"/>).</item>
/// <item>Cada <see cref="SendAsync"/> se difunde a todos como
/// <see cref="ChatHubEventos.MensajeNuevo"/>.</item>
/// <item>Al desconectar se emite "X ha salido del canal" y la lista de
/// usuarios actualizada.</item>
/// </list>
/// El nombre llega como parámetro de ConnectAsync (y no en OnConnectedAsync)
/// porque SignalR conecta primero y "quién eres" se dice después: así el
/// servidor puede rechazar nombres inválidos o duplicados devolviendo un
/// mensaje de error al cliente.
/// </remarks>
/// </summary>
/// <example>
/// ws://localhost:5030/hubs/chat
/// <code>
/// await conexion.InvokeAsync(ChatHubMetodos.Conectar, "Ana");  // → null si OK
/// await conexion.InvokeAsync(ChatHubMetodos.Enviar, "Buenas"); // → null si OK
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
    /// Da de alta al usuario en el canal y le envía el historial.
    /// </summary>
    /// <param name="usuario">Nombre elegido (se recorta y se valida con ChatConfig).</param>
    /// <returns>null si todo fue correcto; el mensaje de error si no (el cliente se desconecta).</returns>
    public async Task<string?> ConnectAsync(string? usuario)
    {
        usuario = usuario?.Trim() ?? string.Empty;

        // Una sola expresión: valida el nombre Y lo registra en el store
        var error = Config.ValidarUsuario(usuario) ?? store.AddUsuario(usuario, Context.ConnectionId);

        if (error is not null)
        {
            logger.LogWarning(
                "Registro rechazado ({ConnectionId}): {Error}", Context.ConnectionId, error);

            return error;
        }

        logger.LogInformation(
            "Usuario {Usuario} conectado al canal {Canal} ({ConnectionId})",
            usuario, Config.Canal, Context.ConnectionId);

        // 1) Historial SOLO a este cliente: no se satura a los demás reenviándolo
        await Clients.Caller.SendAsync(ChatHubEventos.HistorialRecibido, store.GetHistorial());

        // 2) "X se ha unido al canal" a todos (también al que acaba de entrar)
        var mensaje = MensajesSistema.UsuarioUnido(usuario);
        store.AddMensaje(mensaje);
        await Clients.All.SendAsync(ChatHubEventos.MensajeNuevo, mensaje);

        // 3) Lista de usuarios actualizada para todos (punto verde de en línea)
        await Clients.All.SendAsync(ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios());

        return null;
    }

    /// <summary>
    /// Difunde un mensaje del usuario registrado a todos los conectados.
    /// </summary>
    /// <param name="texto">Texto escrito (se valida con ChatConfig).</param>
    /// <returns>null si todo fue correcto; el mensaje de error si no.</returns>
    public async Task<string?> SendAsync(string? texto)
    {
        var usuario = store.GetUsuarioDeConexion(Context.ConnectionId);

        if (usuario is null)
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
            logger.LogWarning("Mensaje rechazado de {Usuario}: {Error}", usuario, error);

            return error;
        }

        var mensaje = new MensajeDto(
            Guid.NewGuid(), usuario, texto, DateTime.UtcNow, TipoMensaje.Normal);

        store.AddMensaje(mensaje);

        // Se loguea la longitud, no el contenido: costumbre de buenas prácticas
        // (los logs pueden verse personas no autorizadas)
        logger.LogInformation(
            "{Usuario} envía mensaje de {Longitud} caracteres", usuario, texto.Length);

        await Clients.All.SendAsync(ChatHubEventos.MensajeNuevo, mensaje);

        return null;
    }

    /// <summary>
    /// Desconexión: si el usuario ya estaba registrado, se despide en su
    /// nombre y se actualiza la lista de conectados.
    /// </summary>
    /// <param name="exception">Excepción que causó la caída, si la hubo.</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var usuario = store.RemoveUsuario(Context.ConnectionId);

        if (usuario is not null)
        {
            if (exception is not null)
                logger.LogWarning(exception,
                    "Usuario {Usuario} se desconectó con error ({ConnectionId})",
                    usuario, Context.ConnectionId);
            else
                logger.LogInformation(
                    "Usuario {Usuario} desconectado ({ConnectionId})",
                    usuario, Context.ConnectionId);

            var mensaje = MensajesSistema.UsuarioSalio(usuario);
            store.AddMensaje(mensaje);

            // Clients.All ya NO incluye al que se fue: el aviso es para el resto
            await Clients.All.SendAsync(ChatHubEventos.MensajeNuevo, mensaje);
            await Clients.All.SendAsync(ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios());
        }
        else
        {
            logger.LogInformation(
                "Conexión sin registrar cerrada: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
