namespace AulaChatNet.Shared.Configuracion;

/// <summary>
/// Opciones tipadas del chat, leídas de la sección "Chat" de appsettings.json.
/// Vive en Shared porque la usan servidor Y clientes: cada proyecto lee SU
/// appsettings (el servidor valida; los clientes solo dan el maxlength y
/// los avisos previos de UX).
///
/// <remarks>
/// Los valores por defecto de esta clase son la última red de seguridad:
/// si un proyecto no tiene la sección "Chat", sigue funcionando con los
/// límites de Fase 1. Nunca se hardcodean límites ni mensajes en el código.
/// </remarks>
/// </summary>
/// <example>
/// appsettings.json:
/// <code>
/// {
///   "Chat": {
///     "Canal": "#general",
///     "UsuarioLongitudMaxima": 20,
///     "HistorialMaximo": 25,
///     "AvatarBaseUrl": "https://api.dicebear.com/10.x"
///   }
/// }
/// </code>
/// </example>
public class ChatConfig
{
    /// <summary>Nombre de la sección de configuración en appsettings.json.</summary>
    public const string Seccion = "Chat";

    /// <summary>Canal único del chat en la Fase 1.</summary>
    public string Canal { get; set; } = "#general";

    /// <summary>Longitud mínima de un nombre de usuario.</summary>
    public int UsuarioLongitudMinima { get; set; } = 2;

    /// <summary>Longitud máxima de un nombre de usuario.</summary>
    public int UsuarioLongitudMaxima { get; set; } = 20;

    /// <summary>Longitud máxima del texto de un mensaje.</summary>
    public int MensajeLongitudMaxima { get; set; } = 500;

    /// <summary>
    /// Nº de mensajes que el servidor conserva y reenvía al conectar
    /// (estilo "no hay mensajes anteriores" de Discord).
    /// </summary>
    public int HistorialMaximo { get; set; } = 25;

    /// <summary>
    /// URL base de DiceBear para los avatares. Configurable para poder
    /// apuntar a un proxy o cache local en producción sin tocar código.
    /// </summary>
    public string AvatarBaseUrl { get; set; } = "https://api.dicebear.com/10.x";

    // ── Mensajes de validación, computados desde la config ──
    // Así, si cambia un límite en appsettings, el mensaje de error
    // que ve el alumno sigue siendo correcto.

    /// <summary>Error cuando el nombre de usuario está vacío.</summary>
    public string ErrorUsuarioVacio => "El nombre de usuario no puede estar vacío";

    /// <summary>Error cuando el nombre de usuario no respeta los límites.</summary>
    public string ErrorUsuarioLongitud =>
        $"El nombre de usuario debe tener entre {UsuarioLongitudMinima} y {UsuarioLongitudMaxima} caracteres";

    /// <summary>Error cuando el nombre de usuario ya lo está usando otro conectado.</summary>
    public string ErrorUsuarioDuplicado => "Ese nombre de usuario ya está en uso, elige otro";

    /// <summary>Error cuando el mensaje está vacío o solo contiene espacios.</summary>
    public string ErrorMensajeVacio => "El mensaje no puede estar vacío";

    /// <summary>Error cuando el mensaje supera la longitud máxima.</summary>
    public string ErrorMensajeLongitud =>
        $"El mensaje no puede superar los {MensajeLongitudMaxima} caracteres";

    // ── Validación ──
    // Un único sitio donde se aplica la regla: el servidor la usa para
    // rechazar peticiones y los clientes para avisar antes de enviar.

    /// <summary>
    /// Valida un nombre de usuario según la configuración.
    /// El llamador debe pasar el nombre ya recortado (Trim()).
    /// </summary>
    /// <param name="usuario">Nombre de usuario a validar (puede ser null).</param>
    /// <returns>null si es válido; el mensaje de error en español si no lo es.</returns>
    public string? ValidarUsuario(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return ErrorUsuarioVacio;

        if (usuario.Length < UsuarioLongitudMinima || usuario.Length > UsuarioLongitudMaxima)
            return ErrorUsuarioLongitud;

        return null;
    }

    /// <summary>
    /// Valida el texto de un mensaje según la configuración.
    /// </summary>
    /// <param name="texto">Texto del mensaje (puede ser null).</param>
    /// <returns>null si es válido; el mensaje de error en español si no lo es.</returns>
    public string? ValidarMensaje(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return ErrorMensajeVacio;

        if (texto.Length > MensajeLongitudMaxima) return ErrorMensajeLongitud;

        return null;
    }
}
