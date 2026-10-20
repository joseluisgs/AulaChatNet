using AulaChatNet.Server.Persistencia;
using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace AulaChatNet.Server.Tests.Services;

/// <summary>
/// Tests de la parte persistente de <see cref="ChatStore"/> (Fase 2): la
/// precarga del historial desde la base de datos y el encolado para escritura
/// en segundo plano. Verifican el comportamiento del store SIN tocar la BD real
/// (el repositorio está mockeado); la escritura real ya la cubren los tests de
/// <c>MensajeRepository</c>.
/// </summary>
[TestFixture]
public class ChatStorePersistenciaTests
{
    private const string General = "#general";
    private const string Smr = "#smr";

    private ChatConfig _config = null!;

    [SetUp]
    public void Setup()
    {
        // HistorialMaximo=10 para poder probar el tope de la precarga
        _config = new ChatConfig { Canales = [General, Smr], HistorialMaximo = 10 };
    }

    /// <summary>Crea un mensaje de prueba con los valores dados.</summary>
    private static MensajeDto Mensaje(string texto) =>
        new(Guid.NewGuid(), "Ana", texto, DateTime.UtcNow, TipoMensaje.Normal);

    /// <summary>
    /// Crea un repositorio mockeado: devuelve <paramref name="paraGeneral"/> para
    /// #general y lista vacía para cualquier otro canal. Así los tests que
    /// recorren varios canales no revientan con null.
    /// </summary>
    private static Mock<IMensajesRepository> CrearRepo(MensajeDto[]? paraGeneral = null)
    {
        var repo = new Mock<IMensajesRepository>();

        repo.Setup(r => r.GetUltimosAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MensajeDto>());

        if (paraGeneral is not null)
            repo.Setup(r => r.GetUltimosAsync(General, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(paraGeneral);

        return repo;
    }

    #region PrecargarAsync

    [Test]
    public async Task Precargar_CargaElHistorialDelRepositorio_ENLaCache()
    {
        // Arrange
        var guardados = new[] { Mensaje("hola"), Mensaje("adios") };
        var store = new ChatStore(Options.Create(_config), CrearRepo(guardados).Object);

        // Act
        var total = await store.PrecargarAsync(10);

        // Assert: el total cuadra y GetHistorial (lectura de caché) los ve
        total.Should().Be(2);
        store.GetHistorial(General).Should().BeEquivalentTo(guardados);
    }

    [Test]
    public async Task Precargar_SinRepositorio_NoHaceNada()
    {
        // Arrange: modo Fase 1, sin repositorio (el parámetro es opcional)
        var store = new ChatStore(Options.Create(_config));

        // Act
        var total = await store.PrecargarAsync(10);

        // Assert
        total.Should().Be(0);
        store.GetHistorial(General).Should().BeEmpty();
    }

    [Test]
    public async Task Precargar_NoPideMasDeLoQueLaCachaRetiene()
    {
        // Arrange: pedimos 50, pero HistorialMaximo es 10 → se piden 10
        var repo = CrearRepo();
        var store = new ChatStore(Options.Create(_config), repo.Object);

        // Act
        await store.PrecargarAsync(50);

        // Assert: el tope aplicado es HistorialMaximo, no el límite pedido
        repo.Verify(r => r.GetUltimosAsync(General, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Precargar_CanalVacio_NoApareceEnLaCache()
    {
        // Arrange: repositorio que devuelve vacío para todo
        var store = new ChatStore(Options.Create(_config), CrearRepo().Object);

        // Act
        var total = await store.PrecargarAsync(10);

        // Assert: nada que cargar y la caché de #general sigue vacía
        total.Should().Be(0);
        store.GetHistorial(General).Should().BeEmpty();
    }

    #endregion

    #region AddMensaje encola para persistir

    [Test]
    public void AddMensaje_ConRepositorio_EncolaElMensajeParaGuardarlo()
    {
        // Arrange
        var store = new ChatStore(Options.Create(_config), CrearRepo().Object);
        var mensaje = Mensaje("persisteme");

        // Act
        store.AddMensaje(General, mensaje);

        // Assert: la cola tiene el mensaje con su canal correcto
        store.ColaEscritura.TryRead(out var pendiente).Should().BeTrue();
        pendiente.Canal.Should().Be(General);
        pendiente.Mensaje.Should().Be(mensaje);
    }

    [Test]
    public void AddMensaje_SinRepositorio_NoEncolaNada()
    {
        // Arrange: modo Fase 1
        var store = new ChatStore(Options.Create(_config));

        // Act
        store.AddMensaje(General, Mensaje("solo memoria"));

        // Assert: la cola queda vacía, pero la memoria SÍ se llenó
        store.ColaEscritura.TryRead(out _).Should().BeFalse();
        store.GetHistorial(General).Should().ContainSingle();
    }

    [Test]
    public void AddMensaje_ConRepositorio_LaMemoriaTambienSeLlena()
    {
        // Arrange
        var store = new ChatStore(Options.Create(_config), CrearRepo().Object);
        var mensaje = Mensaje("en ambos lados");

        // Act
        store.AddMensaje(General, mensaje);

        // Assert: el mensaje está en la caché (GetHistorial) Y en la cola
        store.GetHistorial(General).Should().ContainSingle().Which.Should().Be(mensaje);
        store.ColaEscritura.TryRead(out _).Should().BeTrue();
    }

    #endregion

    #region GetAnterioresAsync (paginación del historial)

    [Test]
    public async Task GetAnteriores_SinRepositorio_DevuelveVacio()
    {
        // Arrange: modo Fase 1. La caché solo guarda lo que el cliente YA
        // ve (los últimos HistorialMaximo): sin disco no hay nada anterior
        var store = new ChatStore(Options.Create(_config));

        // Act
        var pagina = await store.GetAnterioresAsync(General, DateTime.UtcNow, 10);

        // Assert
        pagina.Should().BeEmpty();
    }

    [Test]
    public async Task GetAnteriores_ConCursorPorDefinido_NoTocaElRepositorio()
    {
        // Arrange: un cursor sin fecha (default) no es un momento válido
        var repo = CrearRepo();
        var store = new ChatStore(Options.Create(_config), repo.Object);

        // Act
        var pagina = await store.GetAnterioresAsync(General, default, 10);

        // Assert: ni se consulta la base ni se puede confundir con "todo el
        // historial": no hay nada que paginar sin un punto de partida
        pagina.Should().BeEmpty();
        repo.Verify(r => r.GetAnterioresAsync(
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetAnteriores_PideMasDeLoPermitido_LimitaAlHistorialMaximo()
    {
        // Arrange: HistorialMaximo = 10 en esta suite; alguien pide mil
        var repo = CrearRepo();
        var store = new ChatStore(Options.Create(_config), repo.Object);

        // Act: una página nunca supera la ventana del sistema
        await store.GetAnterioresAsync(General, DateTime.UtcNow, 1_000);

        // Assert
        repo.Verify(r => r.GetAnterioresAsync(
            General, It.IsAny<DateTime>(), 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAnteriores_DevuelveLoQueElRepositorioDiga()
    {
        // Arrange: la store no filtra ni reordena nada, solo limita
        var esperada = new[]
        {
            Mensaje("primero de la página"),
            Mensaje("segundo de la página")
        };

        var repo = new Mock<IMensajesRepository>();
        repo.Setup(r => r.GetAnterioresAsync(General, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(esperada);

        var store = new ChatStore(Options.Create(_config), repo.Object);

        // Act
        var pagina = await store.GetAnterioresAsync(General, DateTime.UtcNow, 5);

        // Assert
        pagina.Should().Equal(esperada);
    }

    #endregion
}
