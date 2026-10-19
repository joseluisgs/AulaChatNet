using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Frase PLANA de las reacciones de un mensaje ("👍 2 (Ana, Bruno)"), para
/// que la terminal y la web describan lo mismo sin duplicar la lógica.
///
/// <remarks>
/// Mismo patrón que <see cref="EscribiendoTexto"/>: Shared calcula el texto
/// (lógica pura, testeable sin abrir UI) y cada cliente lo pinta a su
/// manera — gris al final de la línea en la terminal, chips con contador en
/// la web. El escapado de Spectre NO es asunto suyo: quien pinta decide
/// cómo tratar la cadena.
/// </remarks>
/// </summary>
public static class ReaccionesTexto
{
    /// <summary>
    /// Nombres que se listan junto al contador: con más de tres se cortan
    /// con "..." para que un emoji muy popular no convierta la fila en un
    /// muro de nombres (el número exacto lo pinta cada cliente si quiere).
    /// </summary>
    private const int NombresVisibles = 3;

    /// <summary>
    /// Describe una lista de chips en texto plano, en el orden en que llegan.
    /// </summary>
    /// <param name="reacciones">Chips del mensaje (null o vacío = nada).</param>
    /// <returns>
    /// Vacío si no hay reacciones; si no, un chip por emoji
    /// ("👍 2 (Ana, Bruno)") separados por un espacio.
    /// </returns>
    public static string Descripcion(IReadOnlyList<ReaccionDto>? reacciones)
    {
        if (reacciones is null || reacciones.Count == 0)
            return string.Empty;

        return string.Join(' ', reacciones.Select(Descripcion));
    }

    /// <summary>Un chip concreto: emoji, contador y (si son pocas) quiénes.</summary>
    /// <param name="reaccion">Chip a describir.</param>
    /// <returns>Cadena tipo "👍 2 (Ana, Bruno)".</returns>
    private static string Descripcion(ReaccionDto reaccion)
    {
        var personas = reaccion.Personas;

        if (personas.Count == 0)
            return $"{reaccion.Emoji} 0";

        var visibles = string.Join(", ", personas.Take(NombresVisibles));
        var corte = personas.Count > NombresVisibles ? "..." : string.Empty;

        return $"{reaccion.Emoji} {personas.Count} ({visibles}{corte})";
    }
}
