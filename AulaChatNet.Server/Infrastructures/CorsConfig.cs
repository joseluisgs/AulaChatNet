namespace AulaChatNet.Server.Infrastructures;

/// <summary>
/// Extensiones de configuración de CORS.
///
/// <remarks>
/// En la Fase 1 el servidor aloja al cliente Blazor, así que en el mismo
/// origen no hace falta CORS. Pero en desarrollo, el DevServer de Blazor
/// (dotnet run del proyecto cliente) corre en OTRO puerto que el servidor:
/// a ese tráfico hay que permitirlo.
/// </remarks>
/// </summary>
public static class CorsConfig
{
    private const string Politica = "Blazor";

    /// <summary>
    /// Registra la política CORS a partir de la sección "Cors" del
    /// appsettings. Si la lista de orígenes está vacía se permite cualquier
    /// origen (cómodo en desarrollo); en producción se listan los orígenes
    /// reales del cliente web.
    /// </summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <param name="configuration">Configuración de la aplicación.</param>
    /// <returns>La misma colección, para encadenar.</returns>
    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services, IConfiguration configuration)
    {
        var origenes = configuration
            .GetSection("Cors:OrigenesPermitidos")
            .Get<string[]>() ?? [];

        services.AddCors(opciones => opciones.AddPolicy(Politica, politica =>
        {
            if (origenes.Length == 0)
            {
                // Sin orígenes configurados: cualquiera puede conectarse
                politica.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
            else
            {
                politica.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod();
            }
        }));

        return services;
    }

    /// <summary>
    /// Aplica la política CORS registrada.
    /// </summary>
    /// <param name="app">Constructor de la pipeline de la aplicación.</param>
    /// <returns>El mismo constructor, para encadenar.</returns>
    public static IApplicationBuilder UseCorsPolicy(this IApplicationBuilder app) =>
        app.UseCors(Politica);
}
