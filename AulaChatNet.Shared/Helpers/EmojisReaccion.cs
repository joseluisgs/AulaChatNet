namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Lista CERRADA de emoji que se admiten como reacción, y su validación.
///
/// <remarks>
/// La lista es cerrada a propósito (decisión de diseño de la Fase 3.3):
/// <list type="bullet">
/// <item>El servidor valida con una comparación trivial: "¿está en la lista?"</item>
/// <item>La terminal solo admite emoji de ancho conocido, así que la línea no se descuadra</item>
/// <item>El selector de la web pinta EXACTAMENTE los mismos que admite el servidor</item>
/// <item>Al no haber autenticación, un texto libre pegado en la casilla del
/// emoji ensuciaría el canal para todos; con lista cerrada, el peor caso
/// posible es un emoji de la propia lista</item>
/// </list>
/// Vive en Shared porque la ÚNICA lista válida es la que comparten servidor,
/// terminal y web: si cada uno tuviera la suya, un emoji válido en la web
/// sería un error en la terminal.
/// </remarks>
/// </summary>
/// <example>
/// <code>
/// var emoji = EmojisReaccion.Canonico("👍");      // → "👍"
/// var malo  = EmojisReaccion.Canonico("hola");    // → null
/// </code>
/// </example>
public static class EmojisReaccion
{
    /// <summary>
    /// Los seis emoji admitidos, EN ESTE ORDEN: el orden fijo hace que los
    /// chips de un mensaje no "baile" cuando se añade uno nuevo, y que el
    /// selector de la web y la lista de error de la terminal coincidan.
    /// </summary>
    public static IReadOnlyList<string> Admitidos { get; } = ["👍", "❤️", "😂", "😮", "😢", "😡"];

    // Se calcula una sola vez: se enseña en cada error de emoji no admitido
    private static readonly string ListaCalculada = string.Join(", ", Admitidos);

    /// <summary>Los emoji admitidos como texto, para enseñarlos en un mensaje de error.</summary>
    /// <example>"👍, ❤️, 😂, 😮, 😢, 😡"</example>
    public static string ListaLegible => ListaCalculada;

    /// <summary>
    /// Devuelve el emoji de la lista TAL COMO está en ella, o null si no se
    /// admite. Devolver el CANÓNICO —y no el escrito— garantiza que el chip
    /// pintado sea siempre el mismo: ❤ y ❤️ son el mismo gráfico, pero en
    /// memoria son dos cadenas distintas (❤️ lleva un selector de
    /// presentación al final que se pierde al teclear o al copiar).
    /// </summary>
    /// <param name="emoji">Emoji escrito por el usuario (se recorta; puede ser null).</param>
    /// <returns>El emoji admitido en su forma canónica, o null si no está en la lista.</returns>
    public static string? Canonico(string? emoji)
    {
        var escrito = QuitarSelector(emoji?.Trim() ?? string.Empty);

        if (escrito.Length == 0)
            return null;

        foreach (var admitido in Admitidos)
        {
            if (string.Equals(QuitarSelector(admitido), escrito, StringComparison.Ordinal))
                return admitido;
        }

        return null;
    }

    /// <summary>
    /// Qita el selector de presentación (U+FE0F) de un emoji: ❤ y ❤️ son el
    /// mismo gráfico, pero en memoria son dos cadenas distintas.
    /// </summary>
    /// <param name="texto">Emoji u otra cadena (no null: quien llama ya lo garantiza).</param>
    /// <returns>La misma cadena sin el selector final.</returns>
    private static string QuitarSelector(string texto) => texto.Replace("\uFE0F", string.Empty);
}
