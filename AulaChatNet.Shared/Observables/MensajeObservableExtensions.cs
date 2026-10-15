using System.Reactive.Linq;
using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Shared.Observables;

/// <summary>
/// Operadores Rx de ejemplo aplicados al flujo de mensajes del chat.
/// Son métodos de extensión sobre <c>IObservable&lt;MensajeDto&gt;</c>:
/// así se componen como un pipeline legible, que es la idea central
/// de la programación reactiva con Rx.NET.
///
/// <remarks>
/// Compáralo con el enfoque imperativo: sin Rx, para "ver solo mis mensajes
/// de la última hora" tendrías ifs repartidos por los handlers de eventos.
/// Con Rx, la intención se lee en una cadena.
/// </remarks>
/// </summary>
/// <example>
/// <code>
/// cliente.Mensajes
///     .SoloNormales()
///     .DeUsuario("Ana")
///     .FormatearLinea()
///     .Subscribe(linea => Console.WriteLine(linea));
/// </code>
/// </example>
public static class MensajeObservableExtensions
{
    /// <summary>
    /// Filtra el flujo dejando SOLO los mensajes escritos por usuarios
    /// (descarta los de sistema: entradas y salidas).
    /// </summary>
    /// <param name="origen">Flujo de mensajes entrantes.</param>
    /// <returns>Flujo solo con mensajes de tipo Normal.</returns>
    public static IObservable<MensajeDto> SoloNormales(this IObservable<MensajeDto> origen) =>
        origen.Where(m => m.Tipo == TipoMensaje.Normal);

    /// <summary>
    /// Filtra el flujo dejando SOLO los mensajes de sistema
    /// (uniones y desconexiones: "X se ha unido", "X ha salido").
    /// </summary>
    /// <param name="origen">Flujo de mensajes entrantes.</param>
    /// <returns>Flujo solo con mensajes de tipo Sistema.</returns>
    public static IObservable<MensajeDto> SoloSistema(this IObservable<MensajeDto> origen) =>
        origen.Where(m => m.Tipo == TipoMensaje.Sistema);

    /// <summary>
    /// Filtra el flujo dejando SOLO los mensajes escritos por un usuario concreto.
    /// </summary>
    /// <param name="origen">Flujo de mensajes entrantes.</param>
    /// <param name="usuario">Nombre de usuario a filtrar (comparación insensible a mayúsculas).</param>
    /// <returns>Flujo con los mensajes de ese usuario.</returns>
    public static IObservable<MensajeDto> DeUsuario(this IObservable<MensajeDto> origen, string usuario) =>
        origen.Where(m => string.Equals(m.Usuario, usuario, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Agrupa el flujo por autor: emite un grupo por cada usuario distinto.
    /// Ejemplo didáctico de operador de agrupación (GroupBy).
    /// </summary>
    /// <param name="origen">Flujo de mensajes entrantes.</param>
    /// <returns>Flujo de grupos, cada uno con la clave (autor) y sus mensajes.</returns>
    public static IObservable<IGroupedObservable<string, MensajeDto>> AgruparPorUsuario(
        this IObservable<MensajeDto> origen) =>
        origen.GroupBy(m => m.Usuario);

    /// <summary>
    /// Transforma cada mensaje en una línea de texto formateada
    /// "[HH:mm] Usuario: texto" — ejemplo didáctico de Select (mapeo).
    /// Los mensajes de sistema se marcan con "·".
    /// </summary>
    /// <param name="origen">Flujo de mensajes entrantes.</param>
    /// <returns>Flujo de líneas ya listas para pintar.</returns>
    public static IObservable<string> FormatearLinea(this IObservable<MensajeDto> origen) =>
        origen.Select(m => m.Tipo == TipoMensaje.Sistema
            ? $"[{m.Timestamp:HH:mm}] · {m.Texto}"
            : $"[{m.Timestamp:HH:mm}] {m.Usuario}: {m.Texto}");
}
