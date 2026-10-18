using AulaChatNet.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace AulaChatNet.Server.Infrastructures;

/// <summary>
/// Extensiones de configuración de la PERSISTENCIA (Fase 2): SQLite + EF Core.
/// </summary>
public static class PersistenciaConfigExtensions
{
    /// <summary>
    /// Registra la configuración tipada, el <c>DbContext</c> (mediante fábrica)
    /// y el repositorio de mensajes sobre SQLite.
    /// </summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <param name="configuration">Configuración de la aplicación.</param>
    /// <returns>La misma colección, para encadenar.</returns>
    /// <remarks>
    /// <para>
    /// Se usa <c>AddDbContextFactory</c> y NO <c>AddDbContext</c>. La razón es
    /// de arquitectura: el <c>ChatStore</c> que consumirá el repositorio es un
    /// SINGLETON, y un <c>DbContext</c> es de corta vida (scoped) y no es
    /// thread-safe. La fábrica permite crear un contexto nuevo y desechable por
    /// operación, que es el patrón recomendado cuando un singleton necesita
    /// acceso a EF Core.
    /// </para>
    /// <para>
    /// El repositorio se registra también como singleton por la misma razón:
    /// no guarda estado, solo reparte contextos desde la fábrica.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPersistencia(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Sección "Persistencia" → PersistenciaConfig (con sus valores por
        // defecto como red de seguridad si falta la sección)
        var config = configuration.GetSection(PersistenciaConfig.Seccion).Get<PersistenciaConfig>()
                     ?? new PersistenciaConfig();

        services.Configure<PersistenciaConfig>(
            configuration.GetSection(PersistenciaConfig.Seccion));

        // Asegura que la CARPETA de la base de datos existe. SQLite crea el
        // fichero, pero NO la carpeta que lo contiene: sin este paso, la primera
        // ejecución falla con "unable to open database file".
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(config.Ruta));
        if (!string.IsNullOrWhiteSpace(carpeta))
            Directory.CreateDirectory(carpeta);

        // AddDbContextFactory: la fábrica es singleton y reparte contextos
        // nuevos por operación. Enlaza AulaChatContexto con SQLite usando la
        // cadena de conexión de la configuración.
        services.AddDbContextFactory<AulaChatContexto>(opciones =>
            opciones.UseSqlite(config.CadenaConexion()));

        // Repositorio singleton: no tiene estado propio; cada operación crea y
        // desecha su contexto desde la fábrica.
        services.AddSingleton<IMensajesRepository, MensajeRepository>();

        // Servicio en segundo plano que vacía la cola de escritura del ChatStore
        // y persiste los mensajes. Dependencia implícita: necesita que
        // AddChatServices haya registrado el ChatStore (se llama en Program.cs
        // antes de construir, así que es seguro).
        services.AddHostedService<PersistenciaMensajesService>();

        return services;
    }
}
