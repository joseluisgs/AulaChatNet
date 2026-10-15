using Spectre.Console;

namespace AulaChatNet.Client.Terminal.Consola;

/// <summary>
/// Paleta de colores del chat: la Discord clásica (dark theme) y los colores
/// derivados de cada usuario.
///
/// <remarks>
/// El color de cada nick es una función PURA del nombre (hash estable): misma
/// semilla → mismo color, igual que los avatares de DiceBear. Así Ana siempre
/// es del mismo color en todas las sesiones, sin guardar nada.
///
/// Los hex se crean con <c>Color.FromHex</c> (Spectre.Console 0.57): acepta
/// "#RRGGBB", "RRGGBB" o hasta "RGB". Para meter un color en un string de
/// markup se usa <c>color.ToMarkup()</c>, que sí devuelve "#RRGGBB" con la
/// almohadilla que el markup necesita (<c>ToHex()</c> la omite).
/// </remarks>
/// </summary>
public static class TemaDiscord
{
    /// <summary>Acento (blurple) de Discord.</summary>
    public static readonly Color Blurple = Color.FromHex("#5865f2");

    /// <summary>Verde de "en línea".</summary>
    public static readonly Color Verde = Color.FromHex("#43b581");

    /// <summary>Texto principal.</summary>
    public static readonly Color Texto = Color.FromHex("#dcddde");

    /// <summary>Texto secundario (timestamps, ayuda, prompts).</summary>
    public static readonly Color TextoSecundario = Color.FromHex("#8e9297");

    /// <summary>Fondo principal.</summary>
    public static readonly Color Fondo = Color.FromHex("#36393f");

    /// <summary>Fondo de paneles (sidebar).</summary>
    public static readonly Color GrisOscuro = Color.FromHex("#2f3136");

    /// <summary>Fondo de inputs y cabeceras.</summary>
    public static readonly Color Input = Color.FromHex("#40444b");

    /// <summary>Rojo de errores.</summary>
    public static readonly Color Rojo = Color.FromHex("#f44336");

    /// <summary>
    /// Colores vivos legibles sobre fondo oscuro; el nick se pinta con uno
    /// de ellos según su hash. Sin blancos ni negros (se perderían en el fondo).
    /// </summary>
    private static readonly Color[] Paleta =
    [
        Color.FromHex("#f44336"), // rojo
        Color.FromHex("#ff9800"), // naranja
        Color.FromHex("#ffeb3b"), // amarillo
        Color.FromHex("#43b581"), // verde
        Color.FromHex("#00bcd4"), // cian
        Color.FromHex("#2196f3"), // azul
        Color.FromHex("#9c27b0"), // morado
        Color.FromHex("#e91e63")  // rosa
    ];

    /// <summary>
    /// Devuelve el color estable de un usuario a partir de su nombre.
    /// </summary>
    /// <param name="usuario">Nombre de usuario.</param>
    /// <returns>Color de la paleta; el mismo siempre para el mismo nombre.</returns>
    public static Color ColorDe(string usuario)
    {
        // Hash simple y estable (mismo algoritmo en todas las plataformas y
        // sesiones). La máscara 0x7FFFFFFF evita int.MinValue, que rompería
        // Math.Abs — detalle clásico de los hashes con aritmética entera.
        var hash = 0;

        foreach (var letra in usuario)
            hash = unchecked(hash * 31 + letra);

        return Paleta[(hash & 0x7FFFFFFF) % Paleta.Length];
    }

    /// <summary>
    /// Iniciales estilo Discord entre corchetes: dos letras de dos palabras,
    /// o las dos primeras de una sola ("José Luis" → "JL", "Ana" → "AN").
    /// </summary>
    /// <param name="usuario">Nombre de usuario.</param>
    /// <returns>Iniciales en mayúsculas (mínimo un carácter).</returns>
    public static string Iniciales(string usuario)
    {
        var trozos = usuario.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (trozos.Length == 0)
            return "?";

        if (trozos.Length == 1)
        {
            var palabra = trozos[0];

            return palabra.Length >= 2
                ? palabra[..2].ToUpperInvariant()
                : palabra.ToUpperInvariant();
        }

        return string.Concat(
            trozos[0][..1].ToUpperInvariant(),
            trozos[1][..1].ToUpperInvariant());
    }
}
