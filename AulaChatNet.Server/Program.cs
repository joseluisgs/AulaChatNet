using System.Text;
using AulaChatNet.Server.Infrastructures;
using AulaChatNet.Server.Persistencia;
using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Serilog: la configuración vive entera en la sección "Serilog" de
// appsettings.json (sinks, niveles, rolling de 7 días del fichero de
// errores...), ya fusionada con appsettings.{Entorno}.json.
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

// Persistencia (Fase 2): SQLite + EF Core. Registra la fábrica de contextos y
// el repositorio de mensajes; la carpeta de la base de datos se crea aquí si
// no existe (SQLite crea el fichero, pero no su carpeta).
services.AddPersistencia(configuration);

// SignalR (realtime) + CORS: la web corre en OTRO origen (su DevServer vive en
// el 5031), así que el navegador exige permiso explícito para llamar aquí.
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

// Publicación del hub en la ruta de la config (Chat:HubRuta)
app.MapChatHub(configuration);

// GET /estadisticas: consulta tipo REST con el total y el reparto por sala.
// NO sustituye al evento EstadisticasActualizadas (eso es tiempo real):
// sirve para lo que hay ANTES de que un cliente se conecte al hub, que es
// cuando la terminal todavía quiere decir "actualmente hay 20 personas".
app.MapGet(ChatRutas.Estadisticas, (IChatStore store) =>
    Results.Ok(store.GetEstadisticas()));

// FIN de la API. No hay nada más: este servidor no sirve ficheros estáticos ni
// conoce a ningún cliente. La web vive en su propio proceso y el terminal es
// una app de consola; ambos consumen lo que hay arriba (hub + /estadisticas).

PrintStartupInfo(configuration);

// ============================================================================
// ▶️ ARRANQUE DE LA APLICACIÓN
// ============================================================================

try
{
    // Persistencia al arrancar (Fase 2). Se usa Migrate() y no EnsureCreated():
    // Migrate() aplica SOLO lo que falta y conserva los datos existentes;
    // EnsureCreated() recrearía el esquema y borraría el historial guardado.
    // Se usa un servicio con scope porque el DbContext es de corta vida.
    var persistencia = app.Services.CreateScope().ServiceProvider
        .GetRequiredService<IDbContextFactory<AulaChatContexto>>();

    var opcionesPersistencia = app.Services.GetRequiredService<IOptions<PersistenciaConfig>>().Value;

    // Un único contexto desechable para toda la fase de arranque (borrado + migración)
    await using var contexto = await persistencia.CreateDbContextAsync();

    // Limpieza opcional: si la clave está activa, se BORRA la base de datos
    // entera antes de migrar, para empezar con el historial vacío. Está pensada
    // para desarrollo. El porqué es una clave explícita y no una regla por
    // entorno está explicado en PersistenciaConfig.LimpiarAlArrancar.
    if (opcionesPersistencia.LimpiarAlArrancar)
    {
        var borrada = await contexto.Database.EnsureDeletedAsync();
        Log.Warning(
            "🧹 Arranque limpio (LimpiarAlArrancar=true): base de datos {Estado}. El historial empieza vacío.",
            borrada ? "borrada y recreada" : "no existía, se crea vacía");
    }

    if (opcionesPersistencia.MigrarAutomaticamente)
    {
        var migraciones = await contexto.Database.GetPendingMigrationsAsync();

        if (migraciones.Any())
        {
            await contexto.Database.MigrateAsync();
            Log.Information(
                "💾 Base de datos migrada en {Ruta} ({Pendientes} migraciones aplicadas)",
                opcionesPersistencia.Ruta, migraciones.Count());
        }
        else
        {
            Log.Information("💾 Base de datos al día en {Ruta}", opcionesPersistencia.Ruta);
        }
    }
    else
    {
        // Sin migraciones no hay tablas. Si además se pidió limpiar, el borrado
        // deja la BD sin esquema, así que se avisa alto y claro.
        Log.Warning(
            "💾 Migraciones automáticas DESACTIVADAS (MigrarAutomaticamente=false): " +
            "la base de datos en {Ruta} puede estar desactualizada{Extra}",
            opcionesPersistencia.Ruta,
            opcionesPersistencia.LimpiarAlArrancar
                ? " (Y con LimpiarAlArrancar=true se borró sin recrear el esquema: la app fallará)"
                : string.Empty);
    }

    // Precarga del historial (Fase 2): se traen los últimos mensajes de cada
    // canal de la BD a la caché en memoria, ANTES de aceptar conexiones, para
    // que el primer cliente que entre ya vea el historial. Se usa el ChatStore
    // concreto (no la interfaz) porque PrecargarAsync es un detalle de
    // implementación de la persistencia, no del contrato del hub.
    // Si algo falla (p. ej. esquema sin migrar), se avisa y se arranca con la
    // caché vacía: una precarga no debe tumbar el servidor.
    try
    {
        var store = app.Services.GetRequiredService<ChatStore>();
        var precargados = await store.PrecargarAsync(opcionesPersistencia.PreloadMensajes);
        Log.Information("📚 Historial precargado: {N} mensajes desde la base de datos", precargados);
    }
    catch (Exception ex)
    {
        Log.Warning(ex,
            "No se pudo precargar el historial (¿esquema sin migrar?); se arranca con la caché vacía");
    }

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
    Log.Information("CLIENTES (cada uno en su propio proceso):");
    Log.Information("  Web (Blazor):  dotnet run --project AulaChatNet.Client.Blazor  → :5031");
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
