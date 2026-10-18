using AulaChatNet.Server.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AulaChatNet.Server.Tests.Persistencia;

/// <summary>
/// Base de datos SQLite en memoria COMPARTIDA para los tests del repositorio.
///
/// <remarks>
/// <para>
/// Aquí hay un matiz de SQLite que engaña a mucha gente, y por eso este
/// auxiliar existe. La cadena <c>:memory:</c> llana crea una base de datos
/// VACÍA NUEVA CADA VEZ QUE SE ABRE UNA CONEXIÓN. Como el repositorio usa
/// <c>IDbContextFactory</c> (una conexión distinta por operación), con
/// <c>:memory:</c> llano cada operación vería una base de datos diferente: el
/// test escribiría, luego leería de otra BD vacía, y saldría FALLANDO… o peor,
/// saldría PASSANDO por accidente.
/// </para>
/// <para>
/// La solución es el modo memoria COMPARTIDO: <c>Cache=Shared</c> hace que
/// todas las conexiones con el mismo nombre apunten a la MISMA base de datos
/// en memoria. El truco final es que hay que MANTENER ABIERTA al menos una
/// conexión: cuando la última se cierra, SQLite destruye la base de datos.
/// Por eso este objeto implementa <c>IDisposable</c> y guarda su conexión.
/// </para>
/// </remarks>
/// </summary>
internal sealed class BaseDatosDePrueba : IDisposable
{
    // Nombre único por test: así dos tests en paralelo no pisan su memoria
    private readonly SqliteConnection _conexion;

    /// <summary>
    /// Crea una base de datos en memoria compartida con su esquema ya migrado.
    /// </summary>
    public BaseDatosDePrueba()
    {
        // Nombre aleatorio: cada test tiene su propia base de datos en memoria
        var nombre = $"aulachat-tests-{Guid.NewGuid()}";

        // Cache=Shared es LO QUE HACE que todas las conexiones vean la MISMA BD.
        // Sin eso, cada conexión tendría la suya propia vacía.
        _conexion = new SqliteConnection($"Data Source={nombre};Mode=Memory;Cache=Shared");

        // Obligatoria: si esta conexión se cierra, SQLite destruye la base de datos
        _conexion.Open();

        Opciones = new DbContextOptionsBuilder<AulaChatContexto>()
            .UseSqlite(_conexion)      // se le pasa la CONEXIÓN, no una cadena
            .Options;

        // Crea las tablas e índices declarados en OnModelCreating
        using var contexto = new AulaChatContexto(Opciones);
        contexto.Database.EnsureCreated();
    }

    /// <summary>Opciones del contexto, para construir la fábrica y el repositorio.</summary>
    public DbContextOptions<AulaChatContexto> Opciones { get; }

    /// <summary>
    /// Crea una fábrica de contextos real, igual que la que registrará el
    /// servidor en DI. Así el repositorio de los tests usa el MISMO mecanismo
    /// (un contexto por operación) que en producción.
    /// </summary>
    public IDbContextFactory<AulaChatContexto> CrearFabrica() =>
        new PseudoFabricaDeContextos(Opciones);

    /// <summary>
    /// Fábrica mínima para tests: reparte el MISMO juego de opciones a cada
    /// contexto. En el servidor esto lo hace <c>AddDbContextFactory</c>.
    /// </summary>
    private sealed class PseudoFabricaDeContextos(DbContextOptions<AulaChatContexto> opciones)
        : IDbContextFactory<AulaChatContexto>
    {
        /// <inheritdoc />
        public AulaChatContexto CreateDbContext() => new(opciones);

        /// <inheritdoc />
        public Task<AulaChatContexto> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <inheritdoc />
    public void Dispose() => _conexion.Dispose();
}
