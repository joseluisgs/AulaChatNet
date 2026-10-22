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

    /// <summary>
    /// Canales de texto disponibles, en orden. El primero es el canal por
    /// defecto al conectar. La lista vive en appsettings: añadir o quitar
    /// un aula no requiere tocar código.
    ///
    /// <remarks>
    /// Arranca VACÍA a propósito: el ConfigurationBinder de .NET AÑADE los
    /// valores de appsettings a la lista existente en lugar de reemplazarla,
    /// así que una lista con valores por defecto los duplicaría con los del
    /// fichero. Si un proyecto no tuviera la sección "Chat",
    /// <see cref="CanalPorDefecto"/> sigue ofreciendo "#general".
    /// </remarks>
    /// </summary>
    /// <example>#general, #smr, #daw, #asir, #dam</example>
    public List<string> Canales { get; set; } = [];

    /// <summary>Canal al que entra un cliente que no especifica otro.</summary>
    public string CanalPorDefecto => Canales.Count > 0 ? Canales[0] : "#general";

    /// <summary>
    /// Ruta del endpoint del hub de SignalR. Vive aquí porque la comparten
    /// el servidor (que la publica) y los clientes (que se conectan a ella):
    /// si hay que cambiarla, se cambia en un solo sitio.
    /// </summary>
    /// <example>/hubs/chat</example>
    public string HubRuta { get; set; } = "/hubs/chat";

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
    /// Máximo de mensajes (del canal o privados) que UNA conexión puede
    /// enviar dentro de la ventana de ráfaga. 0 o menos = sin límite
    /// (mismo criterio que RetencionDias en la persistencia).
    /// Solo lo aplica el servidor: el cliente nunca decide la cuota.
    /// </summary>
    public int RafagaMensajes { get; set; } = 5;

    /// <summary>
    /// Longitud, en segundos, de la ventana DESLIZANTE del control de
    /// ráfagas: los intentos más antiguos que esto dejan de contar, así
    /// que la cuota se recarga poco a poco y no cada ventana fija.
    /// </summary>
    public int RafagaVentanaSegundos { get; set; } = 5;

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

    /// <summary>
    /// Aviso cuando una conexión se pasa de la ráfaga máxima. Computado
    /// desde la config: si el centro cambia el límite en appsettings, el
    /// aviso que ve el alumno sigue citando los valores reales.
    /// </summary>
    public string AvisoRafaga =>
        $"Demasiados mensajes seguidos: máximo {RafagaMensajes} cada {RafagaVentanaSegundos} segundos";

    /// <summary>Error cuando un privado no lleva destinatario (invocación directa al hub).</summary>
    public string ErrorPrivadoDestinoVacio => "Falta el destinatario del mensaje privado";

    /// <summary>Error cuando alguien intenta enviarse un privado a sí mismo.</summary>
    public string ErrorPrivadoAUnoMismo => "No puedes enviarte un privado a ti mismo";

    /// <summary>
    /// Error cuando en el canal no hay nadie con ese nombre. Los nombres son
    /// únicos DENTRO de un canal, así que un privado solo puede ir a alguien
    /// de tu propia sala: cruzar canales sería ambiguo sin autenticación.
    /// </summary>
    /// <param name="usuario">Nombre que se buscaba.</param>
    /// <returns>Mensaje en español, sin depender del género del destinatario.</returns>
    public string ErrorPrivadoSinDestino(string usuario) =>
        $"No hay nadie llamado {usuario} conectado en tu canal";

    // ── Validación ──
    // Un único sitio donde se aplica la regla: el servidor la usa para
    // rechazar peticiones y los clientes para avisar antes de enviar.

    /// <summary>
    /// Normaliza un canal escrito por el usuario: recorta los espacios,
    /// añade la almohadilla si falta y pasa a mayúsculas ("smr" → "#SMR").
    /// Así "smr", "#smr" y "#SMR" son el mismo canal.
    /// </summary>
    /// <param name="canal">Canal escrito por el usuario.</param>
    /// <returns>Forma normalizada del canal.</returns>
    public static string NormalizarCanal(string canal)
    {
        var limpio = canal.Trim();
        return (limpio.StartsWith('#') ? limpio : $"#{limpio}").ToUpperInvariant();
    }

    /// <summary>
    /// Devuelve la forma canónica de un canal (la que aparece en la
    /// configuración) si existe; null si no está en la lista.
    /// </summary>
    /// <param name="canal">Canal escrito por el usuario (puede ser null).</param>
    /// <returns>Canal canónico (p. ej. "#smr") o null si no existe.</returns>
    public string? EncontrarCanal(string? canal) =>
        string.IsNullOrWhiteSpace(canal)
            ? null
            : Canales.FirstOrDefault(c =>
                string.Equals(NormalizarCanal(c), NormalizarCanal(canal), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Indica si un canal existe entre los configurados
    /// (insensible a mayúsculas y a la almohadilla inicial).
    /// </summary>
    /// <param name="canal">Canal escrito por el usuario (puede ser null).</param>
    /// <returns>true si el canal está en la lista.</returns>
    public bool EsCanalValido(string? canal) => EncontrarCanal(canal) is not null;

    /// <summary>Error cuando el canal no está en la lista configurada.</summary>
    /// <param name="canal">Canal intentado por el usuario.</param>
    /// <returns>Mensaje en español con los canales válidos.</returns>
    public string ErrorCanalDesconocido(string canal) =>
        $"El canal {canal} no existe. Canales válidos: {string.Join(", ", Canales)}";

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
