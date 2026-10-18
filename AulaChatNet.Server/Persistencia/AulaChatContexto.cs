using Microsoft.EntityFrameworkCore;

namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Contexto de Entity Framework Core del chat: la puerta de entrada a la base
/// de datos SQLite embebida en un solo fichero (<c>aulachat.db</c>).
///
/// <remarks>
/// <para>
/// Es la pieza que convierte "el historial vive en RAM y muere al parar el
/// servidor" en "el historial sobrevive a cualquier reinicio". Su única tabla
/// es <see cref="Mensajes"/>: el resto del estado (quién está conectado, la
/// unicidad de nicks) es EFÍMERO y sigue en el <c>ChatStore</c> en memoria,
/// porque guardar connectionIds de SignalR en disco sería inútil — tras un
/// reinicio ya no significan nada.
/// </para>
/// <para>
/// Como el <c>ChatStore</c> es un singleton y EF Core exige que el
/// <c>DbContext</c> sea de corta vida (scoped), este contexto se registra con
/// <c>AddDbContextFactory</c>: el singleton crea un contexto nuevo por cada
/// operación en vez de compartir uno: compartirlo rompería el hilo de EF Core
/// con concurrencia.
/// </para>
/// </remarks>
/// </summary>
public class AulaChatContexto(DbContextOptions<AulaChatContexto> opciones) : DbContext(opciones)
{
    /// <summary>
    /// Mensajes del chat, de TODOS los canales. La consulta habitual es
    /// "los últimos N de un canal", que resuelve el índice (Canal, Timestamp).
    /// </summary>
    public DbSet<MensajeEntidad> Mensajes => Set<MensajeEntidad>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var mensaje = modelBuilder.Entity<MensajeEntidad>();

        // La tabla se llama como el DbSet y la clave es el GUID que ya trae
        // el MensajeDto desde el servidor: ValueGeneratedNever porque NO la
        // genera la base de datos, ya viene impresa en el propio mensaje.
        mensaje.ToTable("Mensajes");
        mensaje.HasKey(m => m.Id);
        mensaje.Property(m => m.Id).ValueGeneratedNever();

        // Longitudes máximas: SQLite las IGNORA (acepta cualquier texto), pero
        // se declaran porque son el contrato de esquema y porque un día, si se
        // porta a SQL Server o PostgreSQL, sin ellas el esquema saldría raro.
        mensaje.Property(m => m.Canal).IsRequired().HasMaxLength(50);
        mensaje.Property(m => m.Usuario).IsRequired().HasMaxLength(50);
        mensaje.Property(m => m.Texto).IsRequired().HasMaxLength(4_000);

        // El enum TipoMensaje se guarda como entero (0 = Normal, 1 = Sistema):
        // es lo que EF Core hace por defecto, pero declararlo deja constancia
        // del formato del dato en disco y evita sorpresas al leerlo a mano.
        mensaje.Property(m => m.Tipo).HasConversion<int>();

        // Índice 1: la consulta del chat. "Dame los últimos N de #general"
        // ordenados por tiempo. (Canal, Timestamp) hace que esa lectura sea
        // un barrido por un rango ordenado, no un ordenamiento de toda la tabla.
        mensaje.HasIndex(m => new { m.Canal, m.Timestamp });

        // Índice 2: el de la LIMPIEZA. "Borra todo lo anterior a hace 30 días"
        // es una consulta GLOBAL, sin canal, así que el índice anterior no le
        // sirve. Dos consultas distintas, dos índices distintos: los índices se
        // diseñan mirando las consultas, nunca a priori.
        mensaje.HasIndex(m => m.Timestamp);
    }
}
