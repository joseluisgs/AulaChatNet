using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using Serilog;

namespace AulaChatNet.Server.Infrastructures;

/// <summary>
/// Extensiones de configuración de los servicios del chat.
/// </summary>
public static class ServicesConfig
{
    /// <summary>
    /// Registra la configuración tipada del chat y el store en memoria.
    /// </summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <param name="configuration">Configuración de la aplicación.</param>
    /// <returns>La misma colección, para encadenar.</returns>
    public static IServiceCollection AddChatServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        Log.Information("⚙️ Registrando servicios del chat...");

        // Sección "Chat" de appsettings → ChatConfig (la MISMA clase que
        // leen los clientes: límites y rutas sin desviarse)
        services.Configure<ChatConfig>(configuration.GetSection(ChatConfig.Seccion));

        // Singleton: el estado del canal (usuarios e historial) es de TODA
        // la aplicación; todos los hubs comparten la misma instancia
        services.AddSingleton<IChatStore, ChatStore>();

        return services;
    }
}
