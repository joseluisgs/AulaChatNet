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
/// - <see cref="PrivadoRecibido"/> → SOLO al que lo envía y a quien lo recibe
/// - <see cref="Escribiendo"/> → a todos los del canal MENOS al que escribe
/// - <see cref="ReaccionActualizada"/> → a TODO el canal, el emisor incluido
/// - <see cref="ReaccionesRecibidas"/> → solo al que se conecta o cambia de canal
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

    /// <summary>
    /// Entrega un mensaje privado SOLO a las dos personas implicadas: al que
    /// lo escribe (eco, para que su interfaz lo pinte) y al destinatario.
    /// Nunca llega al resto del canal. Argumento: MensajePrivadoDto.
    /// </summary>
    public const string PrivadoRecibido = "PrivadoRecibido";

    /// <summary>
    /// Notifica que alguien del canal está escribiendo, a TODOS MENOS al que
    /// escribe (<c>Clients.OthersInGroup</c>). Es una señal VIVA, sin
    /// contenido: cada cliente la descarta sola a los pocos segundos si no
    /// se renueva. Argumento: string (nombre del que escribe).
    /// </summary>
    public const string Escribiendo = "Escribiendo";

    /// <summary>
    /// Notifica el estado COMPLETO de las reacciones de UN mensaje a TODO el
    /// canal — también al que reaccionó, que debe ver su propio chip.
    /// Argumento: <c>ReaccionesMensajeDto</c>; si sus reacciones vienen
    /// vacías, el mensaje ya no tiene ninguna y hay que borrar la fila.
    /// </summary>
    public const string ReaccionActualizada = "ReaccionActualizada";

    /// <summary>
    /// Entrega SOLO al cliente que se conecta o cambia de canal el estado de
    /// las reacciones de los mensajes que siguen en su historial, para que
    /// quien llega tarde también vea los chips. Argumento:
    /// IReadOnlyList&lt;ReaccionesMensajeDto&gt;.
    /// </summary>
    public const string ReaccionesRecibidas = "ReaccionesRecibidas";
}
