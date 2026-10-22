using AulaChatNet.Client.Blazor.Components.Configuracion;
using AulaChatNet.Client.Blazor.Components.Infrastructures;
using AulaChatNet.Client.Blazor.Components.Services.Storage;   // ILocalStorageService
using AulaChatNet.Client.Wpf.Services;           // FileStorageService
using AulaChatNet.Shared.Configuracion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;   // Replace
using System.Net.Http;   // HttpClient (la variante windows del SDK no lo trae implícito)

namespace AulaChatNet.Client.Wpf.Infrastructures;

/// <summary>
/// Registro de servicios de la app de escritorio (patrón de métodos de
/// extensión, igual que el <c>ServicesConfig</c> del servidor): MISMO
/// esqueleto que el host WebAssembly — ChatConfig, WebConfig, HttpClient y
/// las stores reactivas de la librería compartida— con UNA diferencia de
/// fondo: aquí no existe el "mismo origen", porque no hay página servida,
/// hay un .exe, así que una <c>Web:UrlServidor</c> vacía apunta al servidor
/// local por defecto en lugar de a la URL de la que se cargó la app.
/// </summary>
public static class ServicesConfig
{
    /// <summary>
    /// URL base del servidor que se usa cuando <c>Web:UrlServidor</c> está
    /// vacía o ausente (el servidor de desarrollo escucha aquí).
    /// </summary>
    public const string UrlServidorPorDefecto = "http://localhost:5030";

    /// <summary>
    /// Registra la configuración del chat, el HttpClient de
    /// <c>GET /estadisticas</c> y las stores reactivas apuntando al hub.
    /// </summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <param name="config">Configuración leída de appsettings.json (disco).</param>
    /// <returns>La misma colección, para poder encadenar llamadas.</returns>
    public static IServiceCollection AddClienteWpf(this IServiceCollection services, IConfiguration config)
    {
        // Mismas secciones que en la web: "Chat" viene de Shared (la usa el
        // servidor para validar) y "Web" son ajustes del cliente (la URL del
        // servidor y si recordar sesión)
        var chat = config.GetSection(ChatConfig.Seccion).Get<ChatConfig>() ?? new ChatConfig();
        var web = config.GetSection(WebConfig.Seccion).Get<WebConfig>() ?? new WebConfig();

        services.AddSingleton(chat);
        services.AddSingleton(web);

        var baseServidor = ResolverUrlServidor(web);

        // HttpClient para GET /estadisticas (la misma consulta de bienvenida
        // que hace la terminal antes de registrarse). Timeout corto: la
        // cifra de personas conectadas es opcional, el chat va por SignalR
        services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = new Uri($"{baseServidor}/"),
            Timeout = TimeSpan.FromSeconds(3)
        });

        // Stores reactivas + sesión + procesador de comandos: método de
        // extensión que vive en la RCL, compartido con el host WASM
        services.AddChatStores($"{baseServidor}{chat.HubRuta}");

        // 🖥️ Ajuste del host al medio: la RCL registra LocalStorageService
        // (navegador, JSInterop), pero aquí NO vale — BlazorWebView solo
        // cablea el IJSRuntime de los componentes, y la store singleton
        // usaría una instancia sin cablear y lanzaría "Cannot invoke
        // JavaScript outside of a WebView context" (la barra roja que
        // aparecía al entrar en el chat). Un .exe no tiene navegador: su
        // almacenamiento local es un FICHERO en %LOCALAPPDATA%. Misma
        // interfaz (ILocalStorageService) → la SesionStore de la RCL no
        // cambia ni se entera. Replace cambia el último registro por este.
        services.Replace(ServiceDescriptor
            .Singleton<ILocalStorageService, FileStorageService>());

        // 📥 Bandeja del sistema: la ventana se puede ocultar, pero la app
        // sigue viva y conectada — el icono es el único camino de vuelta y
        // el único camino de salida (con confirmación)
        services.AddSingleton<IBandejaService, BandejaService>();

        // Aviso de "sigue en segundo plano": solo se enseña la primera vez,
        // y la marca queda en el MISMO fichero que la sesión del usuario
        services.AddSingleton<PreferenciasBandeja>();

        return services;
    }

    /// <summary>
    /// Resuelve la URL base del servidor a partir de la configuración. En el
    /// navegador vacío significaba "mismo origen" (la web la descarga el
    /// servidor); aquí ese concepto no existe, así que vacío = el servidor
    /// local por defecto. Con URL escrita, se recorta la barra final para
    /// poder concatenar la ruta del hub sin dobles barras.
    /// </summary>
    /// <param name="web">Opciones del cliente (sección "Web").</param>
    /// <returns>URL base del servidor, sin barra final.</returns>
    public static string ResolverUrlServidor(WebConfig web) =>
        string.IsNullOrWhiteSpace(web.UrlServidor)
            ? UrlServidorPorDefecto
            : web.UrlServidor.TrimEnd('/');
}
