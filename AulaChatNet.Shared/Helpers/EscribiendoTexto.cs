namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// La frase que acompaña al indicador de escritura: "Ana está escribiendo..."
/// con una persona, "Ana, Bruno están escribiendo..." con varias.
///
/// <remarks>
/// Vive en Shared por la MISMA razón que <see cref="CombinadorChat"/>: si la
/// frase se montara en cada cliente, la terminal y la web podrían divergir —
/// y la conjugación (está/están) es el sitio exacto donde eso se nota.
/// El emoji, el color y el escapado de markup los pone cada cliente: esto
/// solo devuelve texto plano.
/// </remarks>
/// </summary>
public static class EscribiendoTexto
{
    /// <summary>Construye la frase de quienes están escribiendo.</summary>
    /// <param name="escribiendo">Nombres, en el orden en que se van a pintar.</param>
    /// <returns>La frase; cadena vacía si no hay nadie.</returns>
    public static string Descripcion(IReadOnlyList<string> escribiendo) =>
        escribiendo.Count switch
        {
            0 => string.Empty,
            1 => $"{escribiendo[0]} está escribiendo...",
            _ => $"{string.Join(", ", escribiendo)} están escribiendo..."
        };
}
