namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Generación de avatares deterministas a partir del nombre de usuario.
/// Usado por ambos clientes: Blazor pinta la URL de DiceBear y terminal
/// las iniciales; los dos resultados son estables para un mismo nombre.
///
/// <remarks>
/// DiceBear (https://www.dicebear.com) es una API pública gratuita que
/// genera avatares SVG a partir de una "semilla": misma semilla → mismo avatar.
/// No requiere autenticación. La URL base NO está hardcodeada: la aporta
/// el cliente desde su appsettings (ChatConfig.AvatarBaseUrl), para poder
/// cambiar de proveedor o apuntar a un proxy en producción.
/// </remarks>
/// </summary>
/// <example>
/// AvatarHelper.ObtenerUrl("Ana", "https://api.dicebear.com/10.x") →
/// https://api.dicebear.com/10.x/adventurer/svg?seed=Ana
/// </example>
public static class AvatarHelper
{
    /// <summary>Estilos de DiceBear entre los que se elige uno por usuario.</summary>
    private static readonly string[] Estilos =
    [
        "adventurer", "avataaars", "bottts", "croodles",
        "fun-emoji", "identicon", "lorelei", "miniavs", "thumbs"
    ];

    /// <summary>
    /// URL del avatar DiceBear para el usuario, con estilo determinista.
    /// </summary>
    /// <param name="usuario">Nombre de usuario (semilla del avatar).</param>
    /// <param name="urlBase">URL base de DiceBear (ChatConfig.AvatarBaseUrl, desde appsettings).</param>
    /// <returns>URL SVG del avatar en la API de DiceBear.</returns>
    public static string ObtenerUrl(string usuario, string urlBase)
    {
        var estilo = ObtenerEstilo(usuario);
        return $"{urlBase.TrimEnd('/')}/{estilo}/svg?seed={Uri.EscapeDataString(usuario)}";
    }

    /// <summary>
    /// Estilo elegido para el usuario. Se calcula con un hash propio
    /// (FNV-1a) en lugar de <c>string.GetHashCode()</c>: ese método NO es
    /// estable entre procesos en .NET, y el avatar cambiaría en cada arranque.
    /// </summary>
    /// <param name="usuario">Nombre de usuario.</param>
    /// <returns>Índice estable dentro de <see cref="Estilos"/>.</returns>
    public static string ObtenerEstilo(string usuario)
    {
        var indice = Math.Abs(HashFnv1a(usuario.ToLowerInvariant())) % Estilos.Length;
        return Estilos[indice];
    }

    /// <summary>
    /// Iniciales del usuario entre corchetes, para la terminal: "[JL]" de "José Luis".
    /// </summary>
    /// <param name="usuario">Nombre de usuario.</param>
    /// <returns>Hasta dos iniciales en mayúsculas entre corchetes.</returns>
    public static string ObtenerIniciales(string usuario)
    {
        var palabras = usuario.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Un solo nombre de palabra: "Ana" → "AN"
        if (palabras.Length == 0) return "[??]";
        if (palabras.Length == 1)
        {
            var una = palabras[0];
            return una.Length >= 2
                ? $"[{una[..2].ToUpperInvariant()}]"
                : $"[{una.ToUpperInvariant()}]";
        }

        // Dos o más palabras: primera letra de las dos primeras, "José Luis" → "JL"
        return $"[{char.ToUpperInvariant(palabras[0][0])}{char.ToUpperInvariant(palabras[1][0])}]";
    }

    /// <summary>
    /// Color RGB derivado del nombre, para pintar el usuario en la terminal
    /// (y el nombre en Blazor). Determinista: mismo nombre → mismo color.
    /// </summary>
    /// <param name="usuario">Nombre de usuario.</param>
    /// <returns>Tupla RGB con valores entre 120 y 255 (colores vivos sobre fondo oscuro).</returns>
    public static (byte R, byte G, byte B) ObtenerColor(string usuario)
    {
        // 🎓 En lugar de usar el hash completo, repartimos el matiz (hue) por el
        // espectro y dejamos G y B más altos para que el texto se lea sobre
        // fondo oscuro (luminosidad alta). Distintos nombres → tonos distintos.
        var hash = HashFnv1a(usuario.ToLowerInvariant());
        var matiz = Math.Abs(hash) % 360;

        return HsvARgb(matiz, 0.65, 0.95);
    }

    /// <summary>
    /// Hash FNV-1a de 32 bits: estable entre procesos y plataformas,
    /// a diferencia de <c>string.GetHashCode()</c>.
    /// </summary>
    private static int HashFnv1a(string texto)
    {
        unchecked
        {
            const uint fnvOffsetBasis = 2166136261;
            const uint fnvPrime = 16777619;

            var hash = fnvOffsetBasis;
            foreach (var c in texto)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return (int)hash;
        }
    }

    /// <summary>Convierte HSV (matiz, saturación, valor) a RGB — solo para colores de consola/UI.</summary>
    private static (byte R, byte G, byte B) HsvARgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
