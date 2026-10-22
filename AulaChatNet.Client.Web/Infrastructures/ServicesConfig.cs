using AulaChatNet.Client.Web.Services;
using AulaChatNet.Client.Web.Services.Storage;
using AulaChatNet.Client.Web.State;
using AulaChatNet.Shared.Configuracion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AulaChatNet.Client.Web.Infrastructures;

/// <summary>
/// Registro de servicios del cliente Blazor (patrón de métodos de extensión,
/// igual que <c>ServicesConfig</c> de la Tienda): Program.cs registra
/// configuración y puntos generales; lo específico del chat vive aquí.
/// </summary>
public static class ServicesConfig
{
    /// <summary>
    /// Registra las stores reactivas del chat, la sesión del navegador y el
    /// procesador de comandos.
    /// </summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <param name="urlHub">URL completa del hub (base del servidor + Chat:HubRuta).</param>
    /// <returns>La misma colección, para poder encadenar llamadas.</returns>
    public static IServiceCollection AddChatStores(this IServiceCollection services, string urlHub)
    {
        // Singleton A PROPÓSITO: la conexión a SignalR es UNA por pestaña.
        // Si la store fuera transitoria, cada componente que la pidiera
        // tendría su propia conexión (y su propio alta en el servidor).
        // GetService (y no GetRequired) para el logger: si algún registro no
        // trae logging (p. ej. tests), la store recibe null y usa NullLogger —
        // un logger jamás es motivo para que no arranque el chat.
        services.AddSingleton<IChatStore>(proveedor =>
            new ChatStore(urlHub,
                proveedor.GetRequiredService<ChatConfig>(),
                proveedor.GetService<ILogger<ChatStore>>()));

        // La sesión (nick y canal) también es única: es el "recuerdo" del
        // navegador entre recargas. Debajo, el servicio de almacenamiento
        // (mismo patrón que la Tienda: ILocalStorageService / LocalStorageService)
        services.AddSingleton<ILocalStorageService, LocalStorageService>();
        services.AddSingleton<ISesionStore>(proveedor =>
            new SesionStore(
                proveedor.GetRequiredService<ILocalStorageService>(),
                proveedor.GetService<ILogger<SesionStore>>()));

        // Procesador de comandos (/canal, /ayuda, /sesion...): MISMO parser
        // que la terminal (Shared), ejecutado con los recursos de la store
        // y la sesión del navegador. Singleton porque solo depende de
        // servicios también únicos por pestaña.
        services.AddSingleton<IProcesadorComandos>(proveedor =>
            new ProcesadorComandos(
                proveedor.GetRequiredService<IChatStore>(),
                proveedor.GetRequiredService<ChatConfig>(),
                proveedor.GetRequiredService<ISesionStore>()));

        return services;
    }
}
