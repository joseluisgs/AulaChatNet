namespace AulaChatNet.Client.Terminal.Comandos;

/// <summary>
/// Representa lo que el usuario quiere hacer: un comando (/canal, /salir...)
/// o un mensaje a enviar al canal actual.
///
/// <remarks>
/// Es un <c>record</c> jerárquico: cada variante se declara dentro del tipo
/// base, y el pattern matching del <c>switch</c> las distingue sin casts ni
/// enums paralelos. Añadir un comando nuevo es añadir una línea aquí y su
/// caso en el parser — nada más.
/// </remarks>
/// </summary>
public abstract record Comando
{
    /// <summary>Texto normal: se envía al canal actual.</summary>
    /// <param name="Texto">Texto escrito por el usuario (ya recortado).</param>
    public sealed record Enviar(string Texto) : Comando;

    /// <summary>/ayuda [tema]: ayuda general o de un comando concreto.</summary>
    /// <param name="Tema">Comando del que pedir ayuda (null = ayuda general).</param>
    public sealed record Ayuda(string? Tema) : Comando;

    /// <summary>/canales: listar los canales disponibles.</summary>
    public sealed record Canales : Comando;

    /// <summary>/canal nombre: cambiar al canal dado.</summary>
    /// <param name="Nombre">Canal escrito por el usuario (p. ej. "daw" o "#daw").</param>
    public sealed record Canal(string Nombre) : Comando;

    /// <summary>/nick nombre: cambiar el nombre en caliente dentro del canal.</summary>
    /// <param name="Nombre">Nuevo nombre escrito por el usuario.</param>
    public sealed record Nick(string Nombre) : Comando;

    /// <summary>/usuarios [canal]: ver los conectados de tu canal o de otro cualquiera.</summary>
    /// <param name="NombreCanal">Canal a consultar (null = el canal actual).</param>
    public sealed record Usuarios(string? NombreCanal = null) : Comando;

    /// <summary>/limpiar: limpiar la vista y repintar desde el búfer local.</summary>
    public sealed record Limpiar : Comando;

    /// <summary>/salir: desconectar y cerrar (Ctrl+C hace exactamente lo mismo).</summary>
    public sealed record Salir : Comando;

    /// <summary>Comando que no se reconoce; se guarda la palabra para repetirla.</summary>
    /// <param name="Palabra">La palabra tal cual la escribió el usuario.</param>
    public sealed record Desconocido(string Palabra) : Comando;

    /// <summary>Comando reconocido pero mal formado (falta un argumento).</summary>
    /// <param name="Motivo">Explicación breve del problema.</param>
    /// <param name="Uso">Ejemplo de uso correcto para enseñarlo al usuario.</param>
    public sealed record Invalido(string Motivo, string Uso) : Comando;
}
