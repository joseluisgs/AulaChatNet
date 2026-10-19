namespace AulaChatNet.Shared.Comandos;

/// <summary>Fila de la ayuda: sintaxis del comando y qué hace.</summary>
/// <param name="Sintaxis">Texto del comando tal y como se escribe (p. ej. "/canal &lt;nombre&gt;").</param>
/// <param name="Descripcion">Explicación corta en español, válida para cualquier cliente.</param>
public sealed record ComandoAyuda(string Sintaxis, string Descripcion);

/// <summary>
/// Catálogo de comandos: la ÚNICA fuente de verdad de la ayuda. Tanto la
/// tabla de la terminal como la ayuda de la web se pintan a partir de aquí,
/// así que añadir un comando es añadir su fila (y su variante y su caso en
/// el parser) sin que las dos ayudas se divergen.
///
/// <remarks>
/// Las descripciones son GENERALES a propósito: lo específico de cada
/// cliente (las teclas de la terminal, por ejemplo) lo añade cada uno en su
/// propia capa de presentación.
/// </remarks>
/// </summary>
public static class AyudaComandos
{
    /// <summary>
    /// Nota que los DOS clientes muestran al abrir un privado: avisa de que
    /// es un mensaje efímero. Vive aquí, y no en cada cliente, por la misma
    /// razón que las descripciones de la ayuda — así los dos dicen
    /// literalmente lo mismo y no puede divergir.
    ///
    /// <remarks>
    /// Se muestra UNA vez (al abrir el privado en la web, en el primer
    /// /privado de la sesión en la terminal), nunca con cada envío: lo que
    /// se repite deja de leerse, y en la terminal cada aviso empuja mensajes
    /// reales fuera de la pantalla.
    /// </remarks>
    /// </summary>
    public const string NotaPrivados =
        "Los privados son efímeros: no se guardan en el servidor ni salen en el historial";

    /// <summary>Comandos y su descripción, en el orden en que se muestran.</summary>
    public static IReadOnlyList<ComandoAyuda> Comandos { get; } =
    [
        new("/ayuda [comando]", "Esta ayuda, o la de un comando concreto"),
        new("/canales", "Lista los canales disponibles y cuánta gente hay en cada uno"),
        new("/canal <nombre>", "Cambia de canal (p. ej. /canal daw)"),
        new("/nick <nombre>", "Cambia tu nombre en caliente (p. ej. /nick Ana2)"),
        new("/usuarios [canal]", "Conectados de tu canal o de otro (p. ej. /usuarios daw)"),
        new("/privado <quien> <texto>", "Mensaje privado a alguien de tu canal (efímero, no se guarda)"),
        new("/reconectar", "Vuelve a entrar si se perdió la conexión"),
        new("/sesion [borrar]", "Ver la sesión guardada o borrarla (útil en aulas compartidas)"),
        new("/limpiar", "Limpia la vista local de mensajes (el historial sigue en el servidor)"),
        new("/salir", "Desconectar y cerrar la sesión")
    ];

    /// <summary>
    /// Busca en el catálogo por prefijo, ignorando la barra inicial:
    /// "canal", "/canal" y "CANAL" piden lo mismo. Sin tema (null o vacío)
    /// devuelve el catálogo completo (la ayuda general).
    /// </summary>
    /// <param name="tema">Comando del que se pide ayuda (sin barra o con ella; null = todo).</param>
    /// <returns>Las filas que encajan; vacío si no hay ayuda para ese tema.</returns>
    public static IReadOnlyList<ComandoAyuda> Buscar(string? tema)
    {
        if (string.IsNullOrWhiteSpace(tema))
            return Comandos;

        var buscado = tema.Trim().TrimStart('/');

        return Comandos
            .Where(c => c.Sintaxis.StartsWith($"/{buscado}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
