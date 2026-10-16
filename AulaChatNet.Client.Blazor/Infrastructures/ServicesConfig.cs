using AulaChatNet.Client.Blazor.Services.Storage;
using AulaChatNet.Client.Blazor.State;
using AulaChatNet.Shared.Configuracion;
using Microsoft.Extensions.DependencyInjection;

namespace AulaChatNet.Client.Blazor.Infrastructures;

/// <summary>
/// Registro de servicios del cliente Blazor (patrón de métodos de extensión,
/// igual que <c>ServicesConfig</c> de la Tienda): Program.cs registra
/// configuración y puntos generales; lo específico del chat vive aquí.
/// </summary>
public static class ServicesConfig
{
    /// <summary>
    /// Registra las stores reactivas del chat y la sesión del navegador.
    /// </summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <param name="urlHub">URL completa del hub (base del servidor + Chat:HubRuta).</param>
    /// <returns>La misma colección, para poder encadenar llamadas.</returns>
    public static IServiceCollection AddChatStores(this IServiceCollection services, string urlHub)
    {
        // Singleton A PROPÓSITO: la conexión a SignalR es UNA por pestaña.
        // Si la store fuera transitoria, cada componente que la pidiera
        // tendría su propia conexión (y su propio alta en el servidor).
        services.AddSingleton<IChatStore>(proveedor =>
            new ChatStore(urlHub, proveedor.GetRequiredService<ChatConfig>()));

        // La sesión (nick y canal) también es única: es el "recuerdo" del
        // navegador entre recargas. Debajo, el servicio de almacenamiento
        // (mismo patrón que la Tienda: ILocalStorageService / LocalStorageService)
        services.AddSingleton<ILocalStorageService, LocalStorageService>();
        services.AddSingleton<ISesionStore, SesionStore>();

        return services;
    }
}
