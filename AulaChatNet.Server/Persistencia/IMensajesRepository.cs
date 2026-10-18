using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Contrato de persistencia del historial del chat (Fase 2). Separa lo DURADERO
/// de lo efímero: el <c>ChatStore</c> sigue guardando en memoria la presencia y
/// la unicidad de nicks (que mueren al reiniciar), y delega aquí SOLO los
/// mensajes, que son lo único que debe sobrevivir a un apagón.
///
/// <remarks>
/// <para>
/// Es asíncrono a diferencia de <c>IChatStore</c>, y esa diferencia es
/// deliberada: un hub de SignalR NUNCA debe bloquear esperando disco, porque
/// con pocos usuarios agotaría el pool de hilos. Por eso el <c>ChatStore</c>
/// (singleton) no llama directamente a EF Core, sino que encola y un servicio
/// en segundo plano hace la escritura.
/// </para>
/// <para>
/// El canal llega como texto canónico (p. ej. "#general"), resuelto ya por el
/// hub con <c>ChatConfig.EncontrarCanal</c>.
/// </para>
/// </remarks>
/// </summary>
public interface IMensajesRepository
{
    /// <summary>
    /// Guarda un mensaje en la base de datos.
    /// </summary>
    /// <param name="canal">Canal canónico donde se escribió.</param>
    /// <param name="mensaje">Mensaje a persistir.</param>
    /// <param name="cancelacion">Token para cancelar la operación (cierre del servidor).</param>
    Task GuardarAsync(string canal, MensajeDto mensaje, CancellationToken cancelacion = default);

    /// <summary>
    /// Devuelve los últimos mensajes de un canal, del más antiguo al más reciente
    /// (mismo orden que devuelve <c>IChatStore.GetHistorial</c>).
    /// </summary>
    /// <param name="canal">Canal canónico.</param>
    /// <param name="limite">Número máximo de mensajes a devolver.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>Lista ordenada cronológicamente, vacía si el canal no tiene mensajes.</returns>
    Task<IReadOnlyList<MensajeDto>> GetUltimosAsync(string canal, int limite, CancellationToken cancelacion = default);

    /// <summary>
    /// Borra los mensajes anteriores a una fecha dada y devuelve cuántos ha quitado.
    /// Es la base del mantenimiento del fichero: sin él, la base de datos crece
    /// sin límite.
    /// </summary>
    /// <param name="anteriorA">Momento (UTC) a partir del cual se conserva todo.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>Número de mensajes eliminados.</returns>
    Task<int> LimpiarAsync(DateTime anteriorA, CancellationToken cancelacion = default);
}
