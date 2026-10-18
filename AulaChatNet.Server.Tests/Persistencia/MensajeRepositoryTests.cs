using AulaChatNet.Server.Persistencia;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AulaChatNet.Server.Tests.Persistencia;

/// <summary>
/// Tests de <see cref="MensajeRepository"/> contra SQLite REAL (en memoria
/// compartida). A diferencia de un proveedor InMemory, aquí se ejecuta SQL de
/// verdad, así que los índices, los tipos de columna y las conversiones
/// (sobre todo el DateTimeOffset) se prueban de verdad.
/// </summary>
[TestFixture]
public class MensajeRepositoryTests
{
    private const string General = "#general";
    private const string Smr = "#smr";

    private BaseDatosDePrueba _bd = null!;
    private MensajeRepository _repo = null!;

    [SetUp]
    public void Setup()
    {
        _bd = new BaseDatosDePrueba();
        _repo = new MensajeRepository(_bd.CrearFabrica(), NullLogger<MensajeRepository>.Instance);
    }

    [TearDown]
    public void TearDown() => _bd.Dispose();

    /// <summary>Crea un mensaje de prueba con los valores dados.</summary>
    private static MensajeDto Mensaje(string texto, TipoMensaje tipo = TipoMensaje.Normal, DateTime? cuando = null) =>
        new(Guid.NewGuid(), "Ana", texto, cuando ?? DateTime.UtcNow, tipo);

    #region GuardarAsync

    [Test]
    public async Task GuardarMensajeNormal_LoRecuperadaDelMismoCanal()
    {
        // Arrange
        var mensaje = Mensaje("Buenas, arrancamos");

        // Act
        await _repo.GuardarAsync(General, mensaje);

        // Assert
        var historial = await _repo.GetUltimosAsync(General, 10);
        historial.Should().ContainSingle()
            .Which.Should().Be(mensaje);
    }

    [Test]
    public async Task GuardarMensajeDeSistema_NoLoPersiste()
    {
        // Arrange: la política de la Fase 2 es NO guardar el ruido de sistema
        await _repo.GuardarAsync(General, Mensaje("Ana ha entrado", TipoMensaje.Sistema));

        // Act
        var historial = await _repo.GetUltimosAsync(General, 10);

        // Assert
        historial.Should().BeEmpty();
    }

    [Test]
    public async Task GuardarMensaje_ElIdDelDtoEsElDeLaBaseDeDatos()
    {
        // Arrange
        var mensaje = Mensaje("con id propio");

        // Act
        await _repo.GuardarAsync(General, mensaje);

        // Assert: el GUID no lo genera la BD, viene impreso en el DTO
        await using var contexto = await _bd.CrearFabrica().CreateDbContextAsync();
        var guardado = await contexto.Mensajes.SingleAsync();
        guardado.Id.Should().Be(mensaje.Id);
    }

    #endregion

    #region GetUltimosAsync

    [Test]
    public async Task GetUltimos_DevuelveElLimitePedidoYEnOrdenCronologico()
    {
        // Arrange: cinco mensajes con marcas de tiempo AÑADIDAS a mano para que
        // el orden sea determinista (si no, dos con el mismo instante se pueden
        // ordenar al revés y el test parpadearía)
        var base_ = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 5; i++)
            await _repo.GuardarAsync(General, Mensaje($"m{i}", cuando: base_.AddMinutes(i)));

        // Act: pedimos solo los DOS últimos
        var historial = await _repo.GetUltimosAsync(General, 2);

        // Assert: el límite se respeta y el orden es del más antiguo al más nuevo
        historial.Should().HaveCount(2);
        historial[0].Texto.Should().Be("m4");
        historial[1].Texto.Should().Be("m5");
    }

    [Test]
    public async Task GetUltimos_LosCanalesNoSeMezclan()
    {
        // Arrange
        await _repo.GuardarAsync(General, Mensaje("de general"));
        await _repo.GuardarAsync(Smr, Mensaje("de smr"));

        // Act
        var historialGeneral = await _repo.GetUltimosAsync(General, 10);

        // Assert
        historialGeneral.Should().ContainSingle().Which.Texto.Should().Be("de general");
    }

    [Test]
    public async Task GetUltimos_CanalSinMensajes_DevuelveVacio()
    {
        // Act
        var historial = await _repo.GetUltimosAsync("#inexistente", 10);

        // Assert
        historial.Should().BeEmpty();
    }

    [Test]
    public async Task GetUltimos_LaFechaVuelveEnUtcYNadaMas()
    {
        // Arrange: mensaje con hora UTC explícita
        var utc = new DateTime(2026, 10, 15, 12, 30, 0, DateTimeKind.Utc);
        await _repo.GuardarAsync(General, Mensaje("hola", cuando: utc));

        // Act
        var historial = await _repo.GetUltimosAsync(General, 1);

        // Assert: SQLite guarda texto, y el DateTimeOffset tiene que devolver
        // el MISMO instante. Es el gotcha de las fechas en SQLite.
        historial.Should().ContainSingle();
        historial[0].Timestamp.Should().Be(utc);
        historial[0].Timestamp.Kind.Should().Be(DateTimeKind.Utc);
    }

    #endregion

    #region LimpiarAsync

    [Test]
    public async Task Limpiar_BorraSoloLoAnteriorALaFechaIndicada()
    {
        // Arrange: uno viejo (2020) y uno reciente (2026)
        var viejo = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var reciente = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        await _repo.GuardarAsync(General, Mensaje("viejo", cuando: viejo));
        await _repo.GuardarAsync(General, Mensaje("reciente", cuando: reciente));

        var corte = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var borrados = await _repo.LimpiarAsync(corte);

        // Assert
        borrados.Should().Be(1);
        var historial = await _repo.GetUltimosAsync(General, 10);
        historial.Should().ContainSingle().Which.Texto.Should().Be("reciente");
    }

    [Test]
    public async Task Limpiar_CuandoNoHayNadaViejo_DevuelveCero()
    {
        // Arrange
        await _repo.GuardarAsync(General, Mensaje("único", cuando: DateTime.UtcNow));

        // Act: corte en el pasado → nada es anterior a eso
        var borrados = await _repo.LimpiarAsync(DateTime.MinValue);

        // Assert
        borrados.Should().Be(0);
        (await _repo.GetUltimosAsync(General, 10)).Should().ContainSingle();
    }

    #endregion

    #region Mantenimiento (fragmentación y VACUUM)

    [Test]
    public async Task Fragmentacion_TrasBorrarMuchosMensajes_SubeY_ALimpiar_VuelveACero()
    {
        // Arrange: rellenar la base con mensajes VIEJOS para que se asignen
        // varias páginas (si no, al borrar no se libera NINGUNA página y no se
        // puede medir la fragmentación de verdad)
        var antiguo = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 400; i++)
            await _repo.GuardarAsync(General, Mensaje(new string('x', 400), cuando: antiguo));

        // Act 1: borrar todo lo anterior a 2021 (es decir, lo que acabamos de meter)
        var borrados = await _repo.LimpiarAsync(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Assert 1: el DELETE marca las páginas como LIBRES, pero no las suelta;
        // ahí es donde aparece la fragmentación
        borrados.Should().Be(400);
        var fragmentada = await _repo.GetFragmentacionAsync();
        fragmentada.Should().BeGreaterThan(0,
            "al borrar masivamente debe quedar espacio libre sin recoger");

        // Act 2: compactar
        await _repo.CompactarAsync();

        // Assert 2: el VACUUM recoge las páginas libres y la base queda limpia
        (await _repo.GetFragmentacionAsync()).Should().Be(0,
            "tras el VACUUM no debe quedar ninguna página libre");
    }

    [Test]
    public async Task Fragmentacion_DevuelveUnPorcentajeEntreCeroYCien()
    {
        // Arrange
        await _repo.GuardarAsync(General, Mensaje("algún mensaje"));

        // Act
        var fragmentacion = await _repo.GetFragmentacionAsync();

        // Assert: es un PORCENTAJE (páginas libres / páginas totales)
        fragmentacion.Should().BeInRange(0, 100);
    }

    [Test]
    public async Task Compactar_DejaLaBaseUtilizable_SiguePudiendoEscribirYLeer()
    {
        // Arrange
        await _repo.GuardarAsync(General, Mensaje("antes del VACUUM"));

        // Act: un VACUUM reescribe el fichero entero, así que hay que
        // comprobar que después la base sigue funcionando con normalidad
        await _repo.CompactarAsync();
        await _repo.GuardarAsync(General, Mensaje("después del VACUUM"));

        // Assert
        var historial = await _repo.GetUltimosAsync(General, 10);
        historial.Should().HaveCount(2);
        historial[^1].Texto.Should().Be("después del VACUUM");
    }

    #endregion

    #region Reanudación (la prueba que justifica la fase entera)

    [Test]
    public async Task TrasReiniciarElProceso_ElHistorialSigueAhi()
    {
        // Arrange
        await _repo.GuardarAsync(General, Mensaje("escrito antes del reinicio"));

        // Act: se SIMULA un reinicio creando un repositorio NUEVO sobre la MISMA
        // base de datos (otra fábrica, otros contextos). En el servidor esto es
        // parar el proceso y arrancarlo de nuevo: el fichero .db permanece.
        var repoTrasReinicio = new MensajeRepository(_bd.CrearFabrica(), NullLogger<MensajeRepository>.Instance);
        var historial = await repoTrasReinicio.GetUltimosAsync(General, 10);

        // Assert
        historial.Should().ContainSingle().Which.Texto.Should().Be("escrito antes del reinicio");
    }

    #endregion
}
