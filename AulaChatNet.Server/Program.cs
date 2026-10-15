using System.Text;
using AulaChatNet.Server.Infrastructures;
using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using Serilog;
using Serilog.Events;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Serilog: TODO se configura en la sección "Serilog" de appsettings.json
// (sinks, niveles, rolling de 7 días del fichero de errores...). La
// configuración llega ya fusionada con appsettings.{Entorno}.json.
Log.Logger = SerilogConfig.Configure(builder.Configuration).CreateLogger();

// Serilog como provider principal de logging (unifica ASP.NET Core + Serilog)
builder.Logging.AddSerilog(Log.Logger);
builder.Host.UseSerilog(Log.Logger);

Log.Information("🚀 Inicializando AulaChatNet.Server...");

// ============================================================================
// 🔧 CONFIGURACIÓN DE SERVICIOS (Extension Methods en Infrastructures)
// ============================================================================

var services = builder.Services;
var configuration = builder.Configuration;

// Opciones tipadas del chat (sección "Chat") + store en memoria (singleton)
services.AddChatServices(configuration);

// SignalR (realtime) + CORS (para el cliente Blazor en desarrollo)
services.AddChatSignalR();
services.AddCorsPolicy(configuration);

// ============================================================================
// 🚀 CONSTRUCCIÓN DE LA APLICACIÓN
// ============================================================================

var app = builder.Build();

Log.Information("✅ Aplicación construida");

// ============================================================================
// 📍 PIPELINE DE MIDDLEWARES
// ============================================================================

// Log de peticiones HTTP. La negociación de SignalR genera una petición por
// cada conexión al hub; la bajamos a Debug para no ensuciar la consola.
app.UseSerilogRequestLogging(opciones =>
{
    opciones.GetLevel = (httpContext, transcurrido, excepcion) =>
        excepcion is not null
            ? LogEventLevel.Error
            : httpContext.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
                ? LogEventLevel.Debug
                : LogEventLevel.Information;
});

app.UseCorsPolicy();

// El servidor alojará también el cliente Blazor (archivos estáticos en
// wwwroot): en la Fase 1 esa carpeta aún está vacía y esto no hace nada.
app.UseDefaultFiles();
app.UseStaticFiles();

// Publicación del hub en la ruta de la config (Chat:HubRuta)
app.MapChatHub(configuration);

// GET /estadisticas: consulta tipo REST con el total y el reparto por sala.
// NO sustituye al evento EstadisticasActualizadas (eso es tiempo real):
// sirve para lo que hay ANTES de que un cliente se conecte al hub, que es
// cuando la terminal todavía quiere decir "actualmente hay 20 personas".
app.MapGet(ChatRutas.Estadisticas, (IChatStore store) =>
    Results.Ok(store.GetEstadisticas()));

PrintStartupInfo(configuration);

// ============================================================================
// ▶️ ARRANQUE DE LA APLICACIÓN
// ============================================================================

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "💥 La aplicación falló al iniciar");
    throw;
}
finally
{
    Log.CloseAndFlush();
}


// <summary>
// Imprime en los logs la información de inicio del servidor.
// </summary>
// <param name="configuration">La configuración de la aplicación.</param>
static void PrintStartupInfo(IConfiguration configuration)
{
    var chat = configuration.GetSection(ChatConfig.Seccion).Get<ChatConfig>()
               ?? new ChatConfig();

    var urls = configuration["ASPNETCORE_URLS"]?.Split(';') ?? ["http://localhost:5030"];
    var primeraUrl = urls.FirstOrDefault() ?? "http://localhost:5030";
    var host = primeraUrl.Contains("://") ? primeraUrl.Split("://")[1].Split(':')[0] : "localhost";
    var puerto = primeraUrl.Contains(':') ? primeraUrl.Split(':').Last() : "5030";

    Log.Information("=================================================================");
    Log.Information("AulaChatNet - Chat en tiempo real para el aula");
    Log.Information("=================================================================");
    Log.Information("CANALES (appsettings.json → sección Chat):");
    Log.Information("  Disponibles:   {Canales}", string.Join(", ", chat.Canales));
    Log.Information("  Por defecto:   {Canal}", chat.CanalPorDefecto);
    Log.Information("SIGNALR (realtime):");
    Log.Information("  Hub del chat:  http://{Host}:{Puerto}{Ruta}", host, puerto, chat.HubRuta);
    Log.Information("CLIENTES:");
    Log.Information("  Web (Blazor):  http://{Host}:{Puerto}/", host, puerto);
    Log.Information("  Terminal:      dotnet run --project AulaChatNet.Client.Terminal");
    Log.Information("LÍMITES (appsettings.json → sección Chat):");
    Log.Information("  Usuario:       entre {Minima} y {Maxima} caracteres",
        chat.UsuarioLongitudMinima, chat.UsuarioLongitudMaxima);
    Log.Information("  Mensaje:       hasta {Maxima} caracteres", chat.MensajeLongitudMaxima);
    Log.Information("  Historial:     últimos {Historial} mensajes", chat.HistorialMaximo);
    Log.Information("LOGGING (appsettings.json → sección Serilog):");
    Log.Information("  Consola:       casi todo (Information)");
    Log.Information("  Fichero:       errores en logs/aulachat-YYYY-MM-DD.log (retención 7 días)");
    Log.Information("=================================================================");
}
