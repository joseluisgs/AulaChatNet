using AulaChatNet.Server.Persistencia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AulaChatNet.Server.Tests.Persistencia;

/// <summary>
/// Tests de orquestación de <see cref="LimpiezaProgramada"/> (Fase 2). Verifican
/// QUÉ decide el servicio —cuándo borra, cuándo compacta, qué pasa si algo
/// falla— con el repositorio mockeado, sin tocar una base de datos real.
///
/// <remarks>
/// La parte que SÍ toca SQLite (fragmentación real y VACUUM) está cubierta en
/// <see cref="MensajeRepositoryTests"/> contra SQLite de verdad. Aquí lo que se
/// prueba es la lógica de decisión, y para eso un mock es mejor: si el
/// servicio decide mal, se ve aquí aunque la base de datos esté sana.
/// </remarks>
/// </summary>
[TestFixture]
public class LimpiezaProgramadaTests
{
    /// <summary>Tiempo máximo de espera en las pruebas de segundo plano.</summary>
    private const int EsperaMaximaMs = 3000;

    private Mock<IMensajesRepository> _repo = null!;
    private PersistenciaConfig _config = null!;
    private LimpiezaProgramada? _servicio;

    [SetUp]
    public void Setup()
    {
        _repo = new Mock<IMensajesRepository>();
        _config = new PersistenciaConfig();
    }

    [TearDown]
    public void TearDown() => _servicio?.Dispose();

    /// <summary>
    /// Crea el servicio con la configuración actual y lo deja guardado para
    /// poder detenerlo/disponerlo en el <c>TearDown</c>.
    /// </summary>
    /// <param name="logger">Logger a usar; por defecto uno que no dice nada.</param>
    private LimpiezaProgramada CrearServicio(ILogger<LimpiezaProgramada>? logger = null)
    {
        _servicio = new LimpiezaProgramada(
            _repo.Object,
            Options.Create(_config),
            logger ?? NullLogger<LimpiezaProgramada>.Instance);

        return _servicio;
    }

    /// <summary>Cuántas veces se ha invocado un método del repositorio mockeado.</summary>
    private int Veces(string metodo) =>
        _repo.Invocations.Count(i => i.Method.Name == metodo);

    /// <summary>
    /// Espera a que se cumpla una condición, con tope de tiempo. Devuelve
    /// <c>false</c> si se agota la espera.
    ///
    /// <remarks>
    /// Hace falta por un detalle de <c>BackgroundService</c> que engaña al
    /// principio: <c>StartAsync</c> NO espera a <c>ExecuteAsync</c>, la lanza
    /// con <c>Task.Run</c> y devuelve al instante. Es decir, inmediatamente
    /// después de <c>StartAsync</c> la primera pasada puede no haber empezado
    /// ni siquiera. Sin esta espera, los tests serían una carrera.
    /// </remarks>
    /// </summary>
    private static async Task<bool> EsperarAsync(Func<bool> condicion)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(EsperaMaximaMs);
        while (DateTime.UtcNow < limite && !condicion())
            await Task.Delay(15);

        return condicion();
    }

    #region Ejecución en segundo plano (ExecuteAsync)

    [Test]
    public async Task ExecuteAsync_ConLimpiezaCadaHorasCero_NoEjecutaNada()
    {
        // Arrange: 0 horas = desactivada a propósito
        _config.LimpiezaCadaHoras = 0;
        var servicio = CrearServicio();

        // Act
        await servicio.StartAsync(CancellationToken.None);

        // Assert: como StartAsync devuelve YA (lanza ExecuteAsync con Task.Run),
        // hay que esperar a que termine. Si el servicio entrara en el bucle de
        // 24 h, esto daría false y el test fallaría.
        var termino = servicio.ExecuteTask!.Wait(TimeSpan.FromMilliseconds(EsperaMaximaMs));

        termino.Should().BeTrue("con LimpiezaCadaHoras = 0 el servicio no debe entrar en el bucle");
        Veces("LimpiarAsync").Should().Be(0);
        Veces("GetFragmentacionAsync").Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_ConRetencionCeroYVacuumDesactivado_NoEjecutaNada()
    {
        // Arrange: no se borra nada (retención ilimitada) y tampoco se compacta
        _config.RetencionDias = 0;
        _config.VacuumSiFragmentado = 0;
        var servicio = CrearServicio();

        // Act
        await servicio.StartAsync(CancellationToken.None);

        // Assert
        var termino = servicio.ExecuteTask!.Wait(TimeSpan.FromMilliseconds(EsperaMaximaMs));

        termino.Should().BeTrue("sin retención y sin VACUUM no hay nada que mantener");
        Veces("LimpiarAsync").Should().Be(0);
        Veces("GetFragmentacionAsync").Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_AlArrancar_EjecutaUnaPasadaInmediata()
    {
        // Arrange
        _config.LimpiezaCadaHoras = 24;
        _config.RetencionDias = 30;
        _config.VacuumSiFragmentado = 25;
        var servicio = CrearServicio();

        // Act
        await servicio.StartAsync(CancellationToken.None);

        // Assert: se espera a que la PRIMERA pasada se ejecute de verdad (va en
        // otro hilo por culpa del Task.Run de StartAsync). Se espera por el
        // ÚLTIMO paso de la pasada para no leer el estado a medias.
        var pasoHecho = await EsperarAsync(() => Veces("GetFragmentacionAsync") >= 1);

        pasoHecho.Should().BeTrue("la primera pasada debe ejecutarse nada más arrancar");
        Veces("LimpiarAsync").Should().Be(1, "la pasada pide borrar exactamente una vez");

        // Y después se queda esperando al siguiente intervalo (24 h): si no,
        // la tarea ya habría terminado y no habría forma de que volviera a limpiar
        servicio.ExecuteTask!.IsCompleted.Should().BeFalse(
            "tras la primera pasada debe esperar al siguiente intervalo");

        await servicio.StopAsync(CancellationToken.None);
    }

    #endregion

    #region Decisión de borrado (LimpiarAsync)

    [Test]
    public async Task LimpiarAsync_ConRetencion_BorraConElCorteDeTreintaDias()
    {
        // Arrange
        _config.RetencionDias = 30;
        _config.VacuumSiFragmentado = 0;         // aislar: solo se prueba el borrado
        var servicio = CrearServicio();

        // Act
        await servicio.LimpiarAsync();

        // Assert: el corte debe ser "ahora menos 30 días" (margen de 1 minuto
        // por si el test tarda algo entre la fecha esperada y la real)
        _repo.Verify(r => r.LimpiarAsync(
            It.Is<DateTime>(d => (DateTime.UtcNow.AddDays(-30) - d).Duration() < TimeSpan.FromMinutes(1)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LimpiarAsync_SinRetencion_NoBorraNada()
    {
        // Arrange: RetencionDias = 0 significa "conservar TODO"
        _config.RetencionDias = 0;
        _config.VacuumSiFragmentado = 0;
        var servicio = CrearServicio();

        // Act
        await servicio.LimpiarAsync();

        // Assert
        _repo.Verify(r => r.LimpiarAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Decisión de compactación (VACUUM)

    [Test]
    public async Task LimpiarAsync_FragmentacionBaja_NoCompacta()
    {
        // Arrange: 10% de páginas libres, por debajo del umbral de 25
        _config.VacuumSiFragmentado = 25;
        _repo.Setup(r => r.GetFragmentacionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(10.0);
        var servicio = CrearServicio();

        // Act
        await servicio.LimpiarAsync();

        // Assert: se midió, pero no se pagó el coste de reescribir la BD
        _repo.Verify(r => r.GetFragmentacionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.CompactarAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task LimpiarAsync_FragmentacionAlta_Compacta()
    {
        // Arrange: 40% de páginas libres, por encima del umbral de 25
        _config.VacuumSiFragmentado = 25;
        _repo.Setup(r => r.GetFragmentacionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(40.0);
        var servicio = CrearServicio();

        // Act
        await servicio.LimpiarAsync();

        // Assert: merecía la pena, así que se ejecuta el VACUUM
        _repo.Verify(r => r.CompactarAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LimpiarAsync_ConVacuumDesactivado_NoNiLoMide()
    {
        // Arrange: umbral 0 = "nunca compactes"
        _config.VacuumSiFragmentado = 0;
        var servicio = CrearServicio();

        // Act
        await servicio.LimpiarAsync();

        // Assert: si nunca se va a compactar, ni siquiera se gasta una consulta
        _repo.Verify(r => r.GetFragmentacionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.CompactarAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Resiliencia (un fallo de mantenimiento no debe caer hacia arriba)

    [Test]
    public async Task LimpiarAsync_SiLaBaseFalla_NoLanzaPeroSiLoRegistra()
    {
        // Arrange: el borrado revienta (disco lleno, base bloqueada…)
        _config.RetencionDias = 30;
        _config.VacuumSiFragmentado = 25;
        _repo.Setup(r => r.LimpiarAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disco lleno"));
        var log = new LoggerDePrueba();
        var servicio = CrearServicio(log);

        // Act: si la excepción se escapara, el propio await haría fallar el test
        await servicio.LimpiarAsync();

        // Assert: no se propaga (el servicio de limpieza jamás debe tumbar nada)
        // PERO tampoco se traga en silencio: queda anotado para diagnosticarlo.
        log.Niveles.Should().Contain(LogLevel.Error,
            "el fallo debe quedarse escrito en el log para poder diagnosticarlo");
        Veces("LimpiarAsync").Should().Be(1, "a pesar del fallo, se llegó a pedir el borrado");
    }

    #endregion

    /// <summary>
    /// Logger mínimo que guarda los niveles que recibe, para poder asertar que
    /// algo se registró. No usa <c>NullLogger</c> porque aquí INTERESA saber
    /// qué se dijo (que el error se logueara es parte del comportamiento).
    /// </summary>
    private sealed class LoggerDePrueba : ILogger<LimpiezaProgramada>
    {
        /// <summary>Niveles de todos los mensajes registrados.</summary>
        public List<LogLevel> Niveles { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Niveles.Add(logLevel);
    }
}
