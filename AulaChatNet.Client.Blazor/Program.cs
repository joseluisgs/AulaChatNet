using AulaChatNet.Client.Blazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// 🎓 Punto de partida: el layout estilo Discord (sidebar + canal #general),
// la store reactiva y la conexión a SignalR se implementarán en su paso
// de la Fase 1. Este scaffold solo deja el cliente en pie.
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();
