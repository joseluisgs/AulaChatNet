using Serilog;

namespace AulaChatNet.Server.Infrastructures;

/// <summary>
/// Extensiones de configuración de Serilog.
///
/// <remarks>
/// Toda la configuración de logging vive en la sección "Serilog" de
/// appsettings.json (sinks, niveles, plantillas, rolling del fichero de
/// errores...): aquí solo se lee esa sección, así se puede cambiar todo
/// SIN recompilar. La configuración llega ya fusionada con
/// appsettings.{Entorno}.json, por lo que cada entorno puede ajustar sus
/// niveles sobrescribiendo claves concretas.
/// </remarks>
/// </summary>
public static class SerilogConfig
{
    /// <summary>
    /// Lee la sección "Serilog" de la configuración y devuelve la
    /// configuración del logger.
    /// </summary>
    /// <param name="configuration">Configuración de la aplicación (builder.Configuration).</param>
    /// <returns>Configuración de logger lista para CreateLogger().</returns>
    public static LoggerConfiguration Configure(IConfiguration configuration) =>
        new LoggerConfiguration().ReadFrom.Configuration(configuration);
}
