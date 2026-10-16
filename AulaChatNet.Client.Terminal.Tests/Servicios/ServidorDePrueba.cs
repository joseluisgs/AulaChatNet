using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace AulaChatNet.Client.Terminal.Tests.Servicios;

/// <summary>
/// Servidor mínimo de mentira para los tests: Kestrel real escuchando en un
/// puerto aleatorio de 127.0.0.1, con un hub que SIEMPRE rechaza el alta
/// (como el servidor real cuando el nick ya está en uso).
///
/// <remarks>
/// No se puede usar el TestServer de <c>Microsoft.AspNetCore.Mvc.Testing</c>:
/// su transporte es en memoria y un cliente SignalR real necesita un HTTP de
/// verdad. Kestrel en un puerto aleatorio sí, y el puerto lo asigna el
/// sistema — así varios tests pueden convivir sin pisarse.
/// </remarks>
/// </summary>
public sealed class ServidorDePrueba : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ServidorDePrueba(WebApplication app) => _app = app;

    /// <summary>URL base donde está escuchando (p. ej. http://127.0.0.1:41234).</summary>
    public string Url => _app.Urls.First();

    /// <summary>
    /// Crea el servidor y lo deja escuchando: factory asíncrono en vez de
    /// constructor, porque arrancar Kestrel no puede hacerse en un ctor.
    /// </summary>
    public static async Task<ServidorDePrueba> CrearAsync()
    {
        var builder = WebApplication.CreateBuilder();

        // Puerto 0 = que el sistema elija uno libre
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Los hubs no funcionan sin estos servicios (MapHub los exige)
        builder.Services.AddSignalR();

        var app = builder.Build();
        app.MapHub<HubQueRechaza>("/hubs/chat"); // misma ruta que el servidor real

        await app.StartAsync();

        return new ServidorDePrueba(app);
    }

    /// <summary>Cierra el servidor (los tests lo hacen con <c>await using</c>).</summary>
    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>
    /// El hub de mentira: reproduce el único caso que nos interesa probar
    /// desde el cliente — el servidor responde con un rechazo.
    /// </summary>
    public sealed class HubQueRechaza : Hub
    {
        /// <summary>Mismo nombre y firma que el hub real: siempre rechaza.</summary>
        /// <param name="usuario">Nick que intenta entrar.</param>
        /// <param name="canal">Canal al que intenta entrar.</param>
        /// <returns>El mensaje de rechazo, como lo devolvería el servidor real.</returns>
        public Task<string?> ConnectAsync(string usuario, string canal) =>
            Task.FromResult<string?>($"Ya hay un {usuario} en {canal}");
    }
}
