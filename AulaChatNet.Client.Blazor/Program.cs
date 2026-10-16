using AulaChatNet.Client.Blazor.Components;
using AulaChatNet.Client.Blazor.Configuracion;
using AulaChatNet.Client.Blazor.Infrastructures;
using AulaChatNet.Shared.Configuracion;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

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
