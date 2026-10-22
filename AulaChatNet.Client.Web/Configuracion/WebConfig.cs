namespace AulaChatNet.Client.Web.Configuracion;

/// <summary>
/// Opciones propias del cliente web, leídas de la sección "Web" de
/// wwwroot/appsettings.json. Vive en este proyecto (no en Shared) porque son
/// ajustes del navegador: el servidor no tiene por qué conocerlos.
///
/// <remarks>
/// En WebAssembly el fichero de configuración es PÚBLICO (cualquiera que
/// abra las herramientas del navegador lo ve): por eso aquí SOLO puede haber
/// ajustes de presentación y conexión. Nunca secretos.
/// </remarks>
/// </summary>
/// <example>
/// appsettings.json:
/// <code>
/// {
///   "Web": {
///     "UrlServidor": "",
///     "RecordarSesion": true
///   }
/// }
/// </code>
/// </example>
public class WebConfig
{
    /// <summary>Nombre de la sección de configuración en appsettings.json.</summary>
    public const string Seccion = "Web";

    /// <summary>
    /// URL base del servidor (sin la ruta del hub). Vacía por defecto = MISMO
    /// ORIGEN: el servidor aloja este cliente, así que la app se conecta a la
    /// misma URL desde la que se cargó y no hay CORS ni URLs hardcodeadas.
    /// Se rellena solo si el cliente se arranca por separado del servidor.
    /// </summary>
    /// <example>http://localhost:5030</example>
    public string UrlServidor { get; set; } = "";

    /// <summary>
    /// Si es true, el nick y el canal elegidos se guardan en localStorage y
    /// se ofrecen la próxima vez (como la sesión de la terminal, pero en el
    /// navegador). Útil para no reescribir el nombre en cada recarga.
    /// </summary>
    public bool RecordarSesion { get; set; } = true;
}
