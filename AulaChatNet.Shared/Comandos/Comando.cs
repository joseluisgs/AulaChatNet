namespace AulaChatNet.Shared.Comandos;

/// <summary>
/// Representa lo que el usuario quiere hacer: un comando (/canal, /salir...)
/// o un mensaje a enviar al canal actual.
///
/// <remarks>
/// Es un <c>record</c> jerárquico: cada variante se declara dentro del tipo
/// base, y el pattern matching del <c>switch</c> las distingue sin casts ni
/// enums paralelos. Añadir un comando nuevo es añadir una línea aquí, su
/// caso en el parser y su fila en <see cref="AyudaComandos"/> — nada más.
///
/// Vive en Shared porque los DOS clientes (terminal y web) interpretan la
/// misma línea escrita del mismo modo: el parser se escribe una vez y cada
/// cliente ejecuta sus variantes con sus propios recursos (Spectre en una
/// punta, stores de Blazor en la otra).
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

    /// <summary>
    /// /privado quien texto: mensaje privado a UNA persona de tu canal.
    /// No se difunde al resto y no se guarda: es efímero.
    /// </summary>
    /// <param name="Destinatario">Nombre de quien lo recibe (en tu canal actual).</param>
    /// <param name="Texto">Contenido del mensaje (mismo límite que un mensaje normal).</param>
    public sealed record Privado(string Destinatario, string Texto) : Comando;

    /// <summary>/reconectar: volver a entrar si la conexión se perdió (la terminal asocia además la tecla R).</summary>
    public sealed record Reconectar : Comando;

    /// <summary>/sesion [borrar]: ver la sesión guardada o borrarla.</summary>
    /// <param name="Accion">Ver lo guardado (por defecto) o borrarlo.</param>
    public sealed record Sesion(AccionSesion Accion) : Comando;

    /// <summary>/limpiar: limpiar la vista local de mensajes (el historial sigue en el servidor).</summary>
    public sealed record Limpiar : Comando;

    /// <summary>/salir: desconectar y cerrar la sesión.</summary>
    public sealed record Salir : Comando;

    /// <summary>Comando que no se reconoce; se guarda la palabra para repetirla.</summary>
    /// <param name="Palabra">La palabra tal cual la escribió el usuario.</param>
    public sealed record Desconocido(string Palabra) : Comando;

    /// <summary>Comando reconocido pero mal formado (falta un argumento).</summary>
    /// <param name="Motivo">Explicación breve del problema.</param>
    /// <param name="Uso">Ejemplo de uso correcto para enseñarlo al usuario.</param>
    public sealed record Invalido(string Motivo, string Uso) : Comando;
}

/// <summary>Acciones que admite el comando /sesion.</summary>
public enum AccionSesion
{
    /// <summary>Ver qué hay guardado en el perfil del usuario.</summary>
    Ver,

    /// <summary>Borrar la sesión guardada (y no volver a guardarla al salir).</summary>
    Borrar
}
