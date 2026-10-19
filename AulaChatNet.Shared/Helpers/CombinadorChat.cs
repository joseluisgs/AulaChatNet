using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Una fila de la vista combinada del chat: un mensaje del canal O uno
/// privado, nunca los dos a la vez.
///
/// <remarks>
/// Es la unión mínima que hace falta para ordenar los DOS flujos juntos sin
/// inventar un tipo común en los DTOs: <see cref="MensajeDto"/> y
/// <see cref="MensajePrivadoDto"/> siguen siendo lo que son, y cada cliente
/// decide cómo pintar cada fila (la terminal con markup, la web con Razor).
/// </remarks>
/// </summary>
/// <param name="EsPrivado">true si la fila es un mensaje privado.</param>
/// <param name="Mensaje">Mensaje del canal (null cuando <paramref name="EsPrivado"/>).</param>
/// <param name="Privado">Mensaje privado (null cuando no lo es).</param>
public sealed record FilaChat(bool EsPrivado, MensajeDto? Mensaje, MensajePrivadoDto? Privado);

/// <summary>
/// Junta el canal y los mensajes privados en UNA sola cronología.
///
/// <remarks>
/// Vive en Shared porque lo necesitan los DOS clientes y debe dar el mismo
/// resultado en ambos: si cada uno ordenara a su manera, la terminal y la web
/// enseñarían el mismo rato de charla en distinto orden.
///
/// Ordenar por <c>Timestamp</c> y no por orden de llegada no es un detalle:
/// el historial del canal llega REEMPLAZADO (mensajes viejos incluidos) y los
/// privados llegan a SU propio ritmo, así que concatenarlos dejaría un
/// privado de las 10:15 por debajo de un mensaje del canal de las 10:20.
/// </remarks>
/// </summary>
public static class CombinadorChat
{
    /// <summary>Combina ambas vistas de la más antigua a la más reciente.</summary>
    /// <param name="canal">Vista del canal (historial + mensajes en vivo).</param>
    /// <param name="privados">Privados recibidos y enviados en este cliente.</param>
    /// <returns>Las dos listas ordenadas juntas, sin perder ninguna de las dos.</returns>
    public static IReadOnlyList<FilaChat> Combinar(
        IReadOnlyList<MensajeDto> canal,
        IReadOnlyList<MensajePrivadoDto> privados)
    {
        ArgumentNullException.ThrowIfNull(canal);
        ArgumentNullException.ThrowIfNull(privados);

        // Atajos para el caso habitual (sin privados, o recién entrados):
        // se ahorra el ordenado de una lista que ya viene ordenada
        if (privados.Count == 0)
            return canal.Select(m => new FilaChat(false, m, null)).ToArray();

        if (canal.Count == 0)
            return privados.Select(p => new FilaChat(true, null, p)).ToArray();

        // OrderBy es ESTABLE: si un privado y un mensaje del canal llevan la
        // misma hora, se queda el orden en que se añadieron (canal primero),
        // así dos mensajes del mismo segundo no se dan la vuelta entre sí
        return canal
            .Select(m => (Momento: m.Timestamp, Fila: new FilaChat(false, m, null)))
            .Concat(privados.Select(p => (Momento: p.Timestamp, Fila: new FilaChat(true, null, p))))
            .OrderBy(x => x.Momento)
            .Select(x => x.Fila)
            .ToArray();
    }
}
