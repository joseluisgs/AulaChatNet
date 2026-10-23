using Serilog;
using Serilog.Events;
using System.IO;   // Path (la variante windows del SDK no lo trae implícito)

namespace AulaChatNet.Client.Wpf.Configuracion;

/// <summary>
/// Configuración de Serilog del cliente de escritorio.
///
/// <remarks>
/// Mismo motor de logging que el resto de la solución, adaptado al medio:
/// aquí solo hay sink de FICHERO, en
/// <c>%LOCALAPPDATA%\AulaChatNet\logs\escritorio-AAAA-MM-DD.log</c>
/// con retención de 7 días (mismo criterio de rolling que la terminal).
///
/// <para>
/// ¿Por qué no consola? Un .exe de Windows arrancado con doble clic NO tiene
/// consola asociada: cualquier WriteTo.Console se perdería (o abriría una
/// terminal en blanco molesta si el binario es consola). Quien necesita el
/// diagnóstico abre el fichero de logs; la ventana ya es su propia vista.
/// </para>
/// </remarks>
/// </summary>
public static class SerilogConfig
{
    /// <summary>
    /// Crea el logger del cliente de escritorio: fichero con rolling diario
    /// y retención de 7 días, nivel Information.
    /// </summary>
    /// <returns>Logger listo para usar (guardarlo en <c>Log.Logger</c>).</returns>
    public static ILogger Crear()
    {
        // Diagnóstico local: va a DatosLocales, no a Roaming (los logs no
        // deben viajar entre equipos del mismo perfil)
        var carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AulaChatNet", "logs");

        return new LoggerConfiguration()
            // Information de serie; subir a Debug cuando haga falta ver las
            // trazas del BlazorWebView (file provider, navegación y código
            // 200/404 de cada fichero servido)
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(carpeta, "escritorio-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
