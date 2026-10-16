using AulaChatNet.Server.Hubs;
using AulaChatNet.Shared.Configuracion;
using Serilog;

namespace AulaChatNet.Server.Infrastructures;

/// <summary>
/// Extensiones de configuración de SignalR para el chat.
/// </summary>
public static class SignalRConfig
{
    /// <summary>
    /// Registra SignalR con los límites adecuados para un chat de aula.
    /// </summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <returns>La misma colección, para encadenar.</returns>
    public static IServiceCollection AddChatSignalR(this IServiceCollection services)
    {
        Log.Information("⚡ Registrando SignalR...");

        services.AddSignalR(opciones =>
        {
            // Límite del mensaje entrante más grande que el de un mensaje de
            // chat: deja margen para el historial que el cliente envía en una
            // sola pieza... aquí el historial va del servidor al cliente
            // (eso no tiene techo), pero un cliente malicioso no podría
            // inyectar payloads gigantes al canal.
            opciones.MaximumReceiveMessageSize = 64 * 1024;
        });

        return services;
    }

    /// <summary>
    /// Publica el <see cref="ChatHub"/> en la ruta configurada en la sección
    /// "Chat" del appsettings (<c>HubRuta</c>), así servidor y clientes
    /// comparten la misma URL sin duplicar el dato.
    /// </summary>
    /// <param name="endpoints">Constructor de endpoints de la aplicación.</param>
    /// <param name="configuration">Configuración de la aplicación.</param>
    /// <returns>El mismo constructor, para encadenar.</returns>
    public static IEndpointRouteBuilder MapChatHub(
        this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        var chat = configuration.GetSection(ChatConfig.Seccion).Get<ChatConfig>()
                   ?? new ChatConfig();

        Log.Information("🔌 Hub de chat publicado en {Ruta}", chat.HubRuta);

        endpoints.MapHub<ChatHub>(chat.HubRuta);

        return endpoints;
    }
}
