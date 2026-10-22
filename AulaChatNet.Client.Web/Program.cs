// Los componentes, las stores y el procesador de comandos viven en la
// librería AulaChatNet.Client.Blazor.Components (compartida con la app de escritorio);
// aquí solo se ARRANCA el host WebAssembly: configuración, logging del
// navegador y registro de servicios.
using AulaChatNet.Client.Blazor.Components;
using AulaChatNet.Client.Blazor.Components.Configuracion;
using AulaChatNet.Client.Blazor.Components.Infrastructures;
using AulaChatNet.Shared.Configuracion;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// ── Configuración ──
// El propio WebAssemblyHostBuilder ya ha leído wwwroot/appsettings.json al
// construirse (su InitializeEnvironment lo hace por convención), así que la
// config está en builder.Configuration. Ojo: en WASM todo lo que aquí se lee
// es PÚBLICO — solo puede haber ajustes de presentación (canales, límites,
// avatar), nunca secretos. La validación real la hace el servidor.
var chat = builder.Configuration
    .GetSection(ChatConfig.Seccion)
    .Get<ChatConfig>() ?? new ChatConfig();

var web = builder.Configuration
    .GetSection(WebConfig.Seccion)
    .Get<WebConfig>() ?? new WebConfig();

builder.Services.AddSingleton(chat);
builder.Services.AddSingleton(web);

// 🔍 Serilog como ÚNICO destino de logging (mismo criterio que el servidor):
// un SOLO logger que ataca por DOS vías — Log.Logger para llamadas estáticas
// y provider de ILogger<T> para las stores (AddLogging garantiza ILogger<> en
// DI, que el host WASM no trae siempre). En WASM no hay disco para ficheros:
// el sink es la CONSOLA del navegador (pestaña "Console" de las herramientas
// de desarrollo), que es donde de verdad se mira el diagnóstico web.
builder.Services.AddLogging();
builder.Logging.ClearProviders();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.BrowserConsole()
    .CreateLogger();

builder.Logging.AddSerilog(Log.Logger, dispose: true);

// Aviso de arranque: la primera línea que se ve en la pestaña Console
// (igual que el "Terminal arrancada" de la cliente de consola)
Log.Information("AulaChatNet web arrancada (entorno {Entorno})",
    builder.HostEnvironment.Environment);

// URL base del servidor. Por defecto (UrlServidor vacío) se usa el MISMO
// ORIGEN desde el que se sirve la app: el servidor aloja el WASM, con lo que
// no hay CORS ni ninguna URL escrita a mano en el código.
var baseServidor = string.IsNullOrWhiteSpace(web.UrlServidor)
    ? builder.HostEnvironment.BaseAddress.TrimEnd('/')
    : web.UrlServidor.TrimEnd('/');

// HttpClient para GET /estadisticas (la misma consulta que hace la terminal
// antes de registrarse). Timeout corto: la cifra de bienvenida es opcional.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri($"{baseServidor}/"),
    Timeout = TimeSpan.FromSeconds(3)
});

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Stores reactivas + sesión en localStorage (método de extensión, estilo Tienda)
builder.Services.AddChatStores($"{baseServidor}{chat.HubRuta}");

await builder.Build().RunAsync();
