using System.Collections.Concurrent;
using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Server.Models;

/// <summary>
/// Estado de UN canal: la unicidad de nombres y el historial son por canal,
/// no globales (puede haber una "Ana" en #general y otra distinta en #smr).
///
/// <remarks>
/// Los usuarios NO se guardan aquí: el índice de conexiones es global
/// (<c>ChatStore._conexiones</c>) y de él se deriva quién hay en cada canal.
/// Así hay UNA sola fuente de verdad de "quién está conectado".
/// </remarks>
/// </summary>
internal sealed class EstadoCanal
{
    /// <summary>Índice de nombres registrados en este canal (clave: nombre normalizado).</summary>
    public ConcurrentDictionary<string, UsuarioConectado> UsuariosPorNombre { get; } = new(StringComparer.Ordinal);

    /// <summary>Historial circular de este canal con el límite de ChatConfig.HistorialMaximo.</summary>
    public ConcurrentQueue<MensajeDto> Historial { get; } = new();

    /// <summary>
    /// Reacciones de los mensajes de ESTE canal: idMensaje → emoji →
    /// personas (clave normalizada → nombre visible tal cual se registró).
    /// Vive POR CANAL porque cada reacción se ancla a SU mensaje, y los
    /// mensajes son de un canal; además se vacía sola: cuando un mensaje
    /// sale de la caché circular, sus reacciones salen con él (lo hace
    /// ChatStore.AddMensaje, que es quien poda la cola).
    /// </summary>
    public ConcurrentDictionary<Guid, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> Reacciones { get; } = new();
}
