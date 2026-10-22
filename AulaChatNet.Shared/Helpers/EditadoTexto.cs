namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Marca de mensaje editado ("(editado)"), para que la terminal y la web
/// pongan EXACTAMENTE el mismo texto sin duplicar la lógica.
///
/// <remarks>
/// Mismo patrón que <see cref="EscribiendoTexto"/> y
/// <see cref="ReaccionesTexto"/>: Shared calcula el texto (lógica pura,
/// testeable sin abrir UI) y cada cliente lo pinta a su manera — gris tras
/// el texto en la terminal, gris tras el mensaje en la web, como el
/// "(editado)" de Discord. El escapado de Spectre NO es asunto suyo: quien
/// pinta decide cómo tratar la cadena.
/// </remarks>
/// </summary>
public static class EditadoTexto
{
    /// <summary>
    /// La marca tal cual: "(editado)". El paréntesis va DENTRO de la
    /// constante para que los dos clientes la pinten idéntica, sin que
    /// nadie tenga que acordarse de añadirlo.
    /// </summary>
    public const string Marca = "(editado)";

    /// <summary>
    /// Devuelve la marca si el mensaje fue editado; cadena vacía si no,
    /// para que quien pinta pueda concatenar sin comprobar nada.
    /// </summary>
    /// <param name="editado">Bandera <c>Editado</c> del mensaje.</param>
    /// <returns>"(editado)" o cadena vacía.</returns>
    public static string Descripcion(bool editado) => editado ? Marca : string.Empty;
}
