using AulaChatNet.Client.Web.Configuracion;   // WebConfig (sección "Web")
using AulaChatNet.Client.Wpf.Configuracion;
using AulaChatNet.Client.Wpf.Infrastructures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.IO;                       // Path (la variante windows del SDK no lo trae implícito)
using System.Windows;                  // Application, StartupEventArgs, ExitEventArgs

namespace AulaChatNet.Client.Wpf;

/// <summary>
/// Punto de entrada de la app de escritorio (WPF + Blazor Hybrid).
/// El orden de arranque importa: primero logging y configuración del DISCO,
/// después la colección de servicios (BlazorWebView + los mismos servicios
/// que el host WebAssembly) y AL FINAL la ventana, que es quien necesita el
/// proveedor ya construido.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Proveedor de servicios compartido. La ventana lo coge para
    /// dárselo al <c>BlazorWebView</c>, que es quien construye los
    /// componentes Razor de la librería compartida.
    /// </summary>
    public static ServiceProvider? Servicios { get; private set; }

    /// <summary>
    /// Configura Serilog, la red de seguridad de excepciones, los servicios
    /// y abre la ventana principal.
    /// </summary>
    /// <param name="e">Argumentos de arranque (no se usan: sin línea de comandos).</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 🔍 Serilog a FICHERO, como la terminal: un .exe de escritorio no
        // tiene consola que leer (doble clic = ventana, punto), así que el
        // diagnóstico va a %LOCALAPPDATA%\AulaChatNet\logs\escritorio-AAAA-MM-DD.log
        Log.Logger = SerilogConfig.Crear();
        Log.Information("AulaChatNet de escritorio arrancando (vía Blazor Hybrid)");

        // 🛡️ Red de seguridad: un fallo no controlado en un hilo de fondo
        // mataría la ventana en silencio; aquí queda registrado antes de morir
        // (patrón recomendado en la documentación de Blazor Hybrid para WPF)
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception,
                "Excepción no controlada ({Tipo})", args.ExceptionObject?.GetType().Name);
            Log.CloseAndFlush();
        };

        // Configuración desde el DISCO: a diferencia del navegador (que la
        // descarga del servidor), aquí el appsettings.json viaja AL LADO del
        // .exe — AppContext.BaseDirectory es la carpeta del ejecutable
        var rutaConfig = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = new ConfigurationBuilder()
            .AddJsonFile(rutaConfig)
            .Build();

        var servicios = new ServiceCollection();

        // BlazorWebView: registra el navegador embebido (WebView2) y el
        // puente de JSInterop. Ojo al nombre: AddWpfBlazorWebView (con el
        // prefijo Wpf) es el del paquete de WPF; sin él, el control falla
        // al aplicar la plantilla con "Unable to find the required services"
        servicios.AddWpfBlazorWebView();

        // Un SOLO destino de logging, igual que en la web: Log.Logger para
        // llamadas estáticas y el provider para los ILogger<T> de DI que
        // piden las stores (AddSerilog puentea ILogger<T> → Serilog)
        servicios.AddLogging(registro => registro.AddSerilog());

        // Mismo esqueleto que el host WASM: ChatConfig, WebConfig, HttpClient
        // del /estadisticas y stores reactivas de la RCL (método de extensión)
        servicios.AddClienteWpf(config);

        Servicios = servicios.BuildServiceProvider();

        // La ventana se crea AQUÍ y no con StartupUri: el BlazorWebView
        // necesita el proveedor de servicios ya construido (App.Servicios)
        var ventana = new MainWindow();
        MainWindow = ventana;
        ventana.Show();

        Log.Information("Ventana principal abierta contra el servidor {Url}",
            Servicios.GetRequiredService<WebConfig>().UrlServidor);
    }

    /// <summary>
    /// Cierra Serilog al salir para no perder los últimos eventos del fichero.
    /// </summary>
    /// <param name="e">Argumentos de salida.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("AulaChatNet de escritorio cerrado");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
