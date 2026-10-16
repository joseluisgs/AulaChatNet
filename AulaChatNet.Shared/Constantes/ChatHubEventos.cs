namespace AulaChatNet.Shared.Constantes;

/// <summary>
/// Nombres de los eventos que el servidor notifica a los clientes.
/// Son los mismos strings que se pasan a <c>HubConnection.On(...)</c> en los
/// clientes y a <c>Clients.All.SendAsync(...)</c> en el servidor: centralizarlos
/// aquí evita el error clásico de escribir mal el nombre del evento.
///
/// <remarks>
/// Eventos del chat:
/// - <see cref="MensajeNuevo"/> → cualquier mensaje (normal o de sistema, p. ej. "X ha salido")
/// - <see cref="ListaUsuariosActualizada"/> → se emite tras cada entrada Y cada desconexión
/// - <see cref="HistorialRecibido"/> → solo al cliente que se conecta (mensajes anteriores)
/// - <see cref="EstadisticasActualizadas"/> → a TODOS: total de conectados y conteo por sala
/// </remarks>
/// </summary>
public static class ChatHubEventos
{
    /// <summary>Notifica un mensaje nuevo (Normal o Sistema). Argumento: MensajeDto.</summary>
    public const string MensajeNuevo = "MensajeNuevo";

    /// <summary>
    /// Notifica la lista completa de usuarios conectados tras un cambio.
    /// Argumento: IReadOnlyList&lt;UsuarioConectadoDto&gt;.
    /// </summary>
    public const string ListaUsuariosActualizada = "ListaUsuariosActualizada";

    /// <summary>
    /// Entrega el historial de los últimos mensajes SOLO al cliente que se conecta.
    /// Argumento: IReadOnlyList&lt;MensajeDto&gt;.
    /// </summary>
    public const string HistorialRecibido = "HistorialRecibido";

    /// <summary>
    /// Notifica las estadísticas globales (total de conectados y conteo por
    /// sala) a TODOS los clientes cuando alguien entra, sale o cambia de sala.
    /// Argumento: EstadisticasDto.
    /// </summary>
    public const string EstadisticasActualizadas = "EstadisticasActualizadas";
}
