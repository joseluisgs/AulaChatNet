using Serilog;
using Serilog.Events;

namespace AulaChatNet.Client.Terminal.Configuracion;

/// <summary>
/// Configuración de Serilog del cliente terminal.
///
/// <remarks>
/// <para>
/// Mismo motor de logging que el servidor, adaptado al medio: aquí solo hay
/// sink de FICHERO, en <c>%LOCALAPPDATA%\AulaChatNet\logs\terminal-AAAA-MM-DD.log</c>
/// con retención de 7 días (igual criterio de rolling que <c>logs/</c> del servidor).
/// </para>
/// <para>
/// ¿Por qué no consola? Porque la vista en vivo de Spectre.Console es un
/// LIENZO que se repinta entero decenas de veces por segundo: cualquier línea
/// escrita por el sink de consola se metería POR ENCIMA del lienzo y la
/// rompería. Quien mira la terminal ya tiene la propia interfaz como registro
/// visual (estados, avisos amarillos, errores rojos); lo que no se ve en la
/// pantalla —la traza de diagnóstico para encontrar fallos— va al fichero.
/// </para>
/// </remarks>
/// </summary>
public static class SerilogConfig
{
    /// <summary>
    /// Crea el logger de la terminal: fichero con rolling diario y
    /// retención de 7 días, nivel Information.
    /// </summary>
    /// <returns>Logger listo para usar (guardarlo en <c>Log.Logger</c>).</returns>
    public static ILogger Crear()
    {
        // Los logs son diagnóstico local: van a DatosLocales, no a Roaming
        // (la sesión de %APPDATA% sí es "estado del usuario" que puede viajar
        // entre equipos; un log no).
        var carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AulaChatNet", "logs");

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(carpeta, "terminal-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
