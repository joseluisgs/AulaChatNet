using AulaChatNet.Server.Persistencia;
using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Tests.Persistencia;

/// <summary>
/// Test de integración de la Fase 2 contra SQLite de FICHERO real (no en
/// memoria). Es la prueba que da sentido a la fase entera: un mensaje enviado
/// DEBE sobrevivir a un reinicio del proceso.
///
/// <remarks>
/// La diferencia clave con <c>MensajeRepositoryTests</c> (que usa memoria
/// compartida) es que aquí la base de datos es un fichero temporal, igual que en
/// producción. Por eso el "reinicio" se puede SIMULAR de verdad: se crea un
/// ChatStore NUEVO sobre el MISMA fichero, que es lo que ocurre al parar y
/// arrancar el servidor. Con memoria no sería posible: muere al cerrar la
/// conexión.
/// </remarks>
/// </summary>
[TestFixture]
public class IntegracionPersistenciaTests
{
    private const string General = "#general";

    private string _ruta = null!;
    private IDbContextFactory<AulaChatContexto> _fabrica = null!;
    private ChatConfig _config = null!;

    [SetUp]
    public async Task Setup()
    {
        // Fichero temporal ÚNICO por test: así no se pisan y se puede borrar al final
        _ruta = Path.Combine(Path.GetTempPath(), $"aulachat-integracion-{Guid.NewGuid()}.db");

        var opciones = new DbContextOptionsBuilder<AulaChatContexto>()
            .UseSqlite($"Data Source={_ruta}")
            .Options;

        _fabrica = new FabricaDeContextos(opciones);

        // Crea el esquema, igual que haría Migrate() al arrancar el servidor
        await using (var contexto = await _fabrica.CreateDbContextAsync())
            await contexto.Database.EnsureCreatedAsync();

        _config = new ChatConfig { Canales = [General], HistorialMaximo = 10 };
    }

    [TearDown]
    public void TearDown()
    {
        // SQLite crea el fichero y sus compañeros -wal y -shm; se borran los tres
        SqliteConnection.ClearAllPools();
        foreach (var sufijo in new[] { "", "-wal", "-shm" })
        {
            var fichero = _ruta + sufijo;
            if (File.Exists(fichero))
                File.Delete(fichero);
        }
    }

    /// <summary>Crea un mensaje de prueba con los valores dados.</summary>
    private static MensajeDto Mensaje(string texto, TipoMensaje tipo = TipoMensaje.Normal) =>
        new(Guid.NewGuid(), "Ana", texto, DateTime.UtcNow, tipo);

    /// <summary>Crea un repositorio real apuntando a la BD de prueba.</summary>
    private MensajeRepository CrearRepositorio() =>
        new(_fabrica, NullLogger<MensajeRepository>.Instance);

    /// <summary>
    /// Simula lo que hace el <c>PersistenciaMensajesService</c> en segundo
    /// plano: vacía la cola de escritura del store contra la base de datos.
    /// </summary>
    private static async Task SimularServicioDeFondo(ChatStore store, MensajeRepository repo)
    {
        while (store.ColaEscritura.TryRead(out var pendiente))
            await repo.GuardarAsync(pendiente.Canal, pendiente.Mensaje);
    }

    [Test]
    public async Task MensajeEnviado_SobreviveAUnReinicioDelProceso()
    {
        // Arrange: el store de la "primera sesión"
        var store = new ChatStore(Options.Create(_config), CrearRepositorio());
        var mensaje = Mensaje("escrito antes del reinicio");

        // Act 1: el cliente envía el mensaje (AddMensaje solo encola)
        store.AddMensaje(General, mensaje);

        // Act 2: el servicio de fondo lo persiste en el fichero
        await SimularServicioDeFondo(store, CrearRepositorio());

        // Act 3: REINICIO — un store NUEVO sobre el MISMA base de datos,
        // que es lo que ocurre al parar y arrancar el servidor
        var storeTrasReinicio = new ChatStore(Options.Create(_config), CrearRepositorio());
        var precargados = await storeTrasReinicio.PrecargarAsync(10);

        // Assert: el mensaje que se envió antes de reiniciar está en la caché
        precargados.Should().Be(1);
        storeTrasReinicio.GetHistorial(General).Should().ContainSingle()
            .Which.Texto.Should().Be("escrito antes del reinicio");
    }

    [Test]
    public async Task VariosMensajes_SobrevivenAUnReinicio_ENOrdenCronologico()
    {
        // Arrange
        var store = new ChatStore(Options.Create(_config), CrearRepositorio());

        // Act: tres mensajes en orden y persistencia simulada
        store.AddMensaje(General, Mensaje("primero"));
        store.AddMensaje(General, Mensaje("segundo"));
        store.AddMensaje(General, Mensaje("tercero"));
        await SimularServicioDeFondo(store, CrearRepositorio());

        // Reinicio y precarga
        var storeTrasReinicio = new ChatStore(Options.Create(_config), CrearRepositorio());
        await storeTrasReinicio.PrecargarAsync(10);

        // Assert: llegan los tres, del más antiguo al más reciente
        var historial = storeTrasReinicio.GetHistorial(General);
        historial.Should().HaveCount(3);
        historial.Select(m => m.Texto).Should().ContainInOrder("primero", "segundo", "tercero");
    }

    [Test]
    public async Task MensajeDeSistema_NoSobreviveAUnReinicio()
    {
        // Arrange: política de la Fase 2 — el ruido de sistema no se persiste
        var store = new ChatStore(Options.Create(_config), CrearRepositorio());
        store.AddMensaje(General, Mensaje("Ana ha entrado", TipoMensaje.Sistema));
        await SimularServicioDeFondo(store, CrearRepositorio());

        // Act: reinicio y precarga
        var storeTrasReinicio = new ChatStore(Options.Create(_config), CrearRepositorio());
        var precargados = await storeTrasReinicio.PrecargarAsync(10);

        // Assert: el mensaje de sistema quedó solo en la caché de la sesión anterior
        precargados.Should().Be(0);
        storeTrasReinicio.GetHistorial(General).Should().BeEmpty();
    }

    #region Limpieza programada (Fase 6) contra el fichero real

    [Test]
    public async Task LimpiezaProgramada_ContraElFicheroReal_BorraLoViejo_YDejaLaBaseSinFragmentar()
    {
        // Arrange: 400 mensajes VIEJOS (de hace 100 días) con texto largo, para
        // que al borrarlos se liberen de verdad páginas enteras; y uno RECIENTE,
        // que es el que tiene que seguir ahí al final.
        var repo = CrearRepositorio();
        var antiguo = DateTime.UtcNow.AddDays(-100);

        for (var i = 0; i < 400; i++)
            await repo.GuardarAsync(General,
                new MensajeDto(Guid.NewGuid(), "Ana", new string('x', 400), antiguo, TipoMensaje.Normal));

        await repo.GuardarAsync(General,
            new MensajeDto(Guid.NewGuid(), "Ana", "reciente", DateTime.UtcNow, TipoMensaje.Normal));

        // La retención es de 30 días: lo de hace 100 se va, lo de hoy se queda.
        var config = new PersistenciaConfig { RetencionDias = 30, VacuumSiFragmentado = 25 };
        var servicio = new LimpiezaProgramada(
            repo, Options.Create(config), NullLogger<LimpiezaProgramada>.Instance);

        // Act: UNA pasada completa — borrar, medir la fragmentación y compactar
        await servicio.LimpiarAsync();

        // Assert 1: se fue lo viejo y sobrevivió lo reciente...
        var historial = await repo.GetUltimosAsync(General, 10);
        historial.Should().ContainSingle()
            .Which.Texto.Should().Be("reciente", "la retención es de 30 días y ese mensaje es de hoy");

        // Assert 2: ...y la base quedó SIN páginas libres. Esto solo es posible
        // si el VACUUM llegó a ejecutarse, porque el DELETE por sí solo deja las
        // páginas marcadas como libres (fragmentación > 0) sin devolver nada.
        (await repo.GetFragmentacionAsync()).Should().Be(0,
            "el borrado masivo superó el umbral, así que debía compactarse");
    }

    #endregion

    /// <summary>
    /// Fábrica mínima de contextos para el test: reparte el mismo juego de
    /// opciones a cada contexto, igual que <c>AddDbContextFactory</c> en DI.
    /// </summary>
    private sealed class FabricaDeContextos(DbContextOptions<AulaChatContexto> opciones)
        : IDbContextFactory<AulaChatContexto>
    {
        /// <inheritdoc />
        public AulaChatContexto CreateDbContext() => new(opciones);

        /// <inheritdoc />
        public Task<AulaChatContexto> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
