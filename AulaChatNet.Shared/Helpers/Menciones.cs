using System.Text.RegularExpressions;

namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Detección de menciones (@ana) en el texto de un mensaje. Vive en Shared
/// porque la MISMA regla la aplican el servidor (para avisar a quien nombra)
/// y los clientes (para resaltar la mención al pintar): si la detección
/// viviera en dos sitios, acabarían desviándose y un @ podría ser mención en
/// la web y no serlo en la terminal.
///
/// <remarks>
/// Límite honesto: un nick con ESPACIOS no se puede mencionar — el token se
/// corta en el primer carácter que no sea letra, dígito, guion bajo o
/// guion—, igual que en Discord. Los nicks del proyecto (2 a 20 caracteres,
/// sin espacios) no tienen ese problema.
/// </remarks>
/// </summary>
public static partial class Menciones
{
    /// <summary>
    /// Patrón de mención: una arroba seguida de letras, dígitos, guion bajo
    /// o guion. El token se corta en cuanto aparece cualquier otro
    /// carácter, así que "@ana," o "@luis!" reconocen a "ana" y a "luis".
    /// </summary>
    [GeneratedRegex(@"@[\p{L}\p{N}_-]+")]
    private static partial Regex Patron();

    /// <summary>
    /// Extrae los nombres mencionados (SIN la @), en orden de aparición y
    /// sin repetidos: "@Ana @ana hola" menciona a Ana una sola vez.
    /// </summary>
    /// <param name="texto">Texto del mensaje (o null).</param>
    /// <returns>Nombres tal y como se escribieron (comparar con <c>OrdinalIgnoreCase</c>).</returns>
    public static IReadOnlyList<string> Extraer(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return [];

        var nombres = new List<string>();

        foreach (Match coincidencia in Patron().Matches(texto))
        {
            var nombre = coincidencia.Value[1..]; // sin la @

            if (!nombres.Contains(nombre, StringComparer.OrdinalIgnoreCase))
                nombres.Add(nombre);
        }

        return nombres;
    }

    /// <summary>
    /// ¿El texto menciona a esa persona? Comparación insensible a
    /// mayúsculas, la misma regla que nicks, canales y privados.
    /// </summary>
    /// <param name="texto">Texto del mensaje (o null).</param>
    /// <param name="usuario">Nombre de la persona buscada.</param>
    /// <returns>true si el texto la nombra con @.</returns>
    public static bool Contiene(string? texto, string usuario) =>
        Extraer(texto).Contains(usuario, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Trocea el texto en tramos, marcando cuáles son MENCIONES, para que
    /// cada cliente los pinte resaltados sin reimplementar la detección:
    /// Shared manda la FORMA, los clientes solo el color.
    /// </summary>
    /// <param name="texto">Texto del mensaje (o null).</param>
    /// <returns>Tramos en orden; los de mención llevan <c>EsMencion</c> a true.</returns>
    public static IReadOnlyList<(string Texto, bool EsMencion)> Trocear(string? texto)
    {
        var tramos = new List<(string, bool)>();

        if (string.IsNullOrEmpty(texto))
            return tramos;

        var ultima = 0;

        foreach (Match coincidencia in Patron().Matches(texto))
        {
            if (coincidencia.Index > ultima)
                tramos.Add((texto[ultima..coincidencia.Index], false));

            tramos.Add((coincidencia.Value, true));
            ultima = coincidencia.Index + coincidencia.Length;
        }

        if (ultima < texto.Length)
            tramos.Add((texto[ultima..], false));

        return tramos;
    }
}
