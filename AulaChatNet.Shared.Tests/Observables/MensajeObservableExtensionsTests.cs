using System.Reactive.Subjects;
using FluentAssertions;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Observables;

namespace AulaChatNet.Shared.Tests.Observables;

/// <summary>
/// Suite de tests para los operadores Rx de MensajeObservableExtensions.
/// Usa un Subject como origen simulado del flujo de mensajes del hub:
/// Subject emite de forma síncrona, así los tests no necesitan esperas.
/// </summary>
[TestFixture]
public class MensajeObservableExtensionsTests
{
    private Subject<MensajeDto> _origen = null!;
    private List<MensajeDto> _recibidos = null!;

    [SetUp]
    public void Setup()
    {
        _origen = new Subject<MensajeDto>();
        _recibidos = [];
    }

    [TearDown]
    public void TearDown() => _origen.Dispose();

    /// <summary>Crea un mensaje de prueba con los valores dados.</summary>
    private static MensajeDto CrearMensaje(
        string usuario = "Ana",
        string texto = "hola",
        TipoMensaje tipo = TipoMensaje.Normal) =>
        new(Guid.NewGuid(), usuario, texto, new DateTime(2026, 1, 1, 10, 30, 0, DateTimeKind.Utc), tipo);

    #region SoloNormales Tests

    [Test]
    public void SoloNormales_FlujoMixto_FiltraLosMensajesDeSistema()
    {
        // Arrange
        _origen.SoloNormales().Subscribe(_recibidos.Add);
        var normal = CrearMensaje(texto: "Buenas");
        var sistema = CrearMensaje(tipo: TipoMensaje.Sistema);

        // Act
        _origen.OnNext(normal);
        _origen.OnNext(sistema);

        // Assert
        _recibidos.Should().ContainSingle().Which.Should().Be(normal);
    }

    #endregion

    #region SoloSistema Tests

    [Test]
    public void SoloSistema_FlujoMixto_FiltraLosMensajesNormales()
    {
        // Arrange
        _origen.SoloSistema().Subscribe(_recibidos.Add);
        var sistema1 = CrearMensaje(tipo: TipoMensaje.Sistema);
        var sistema2 = CrearMensaje(usuario: "Luis", texto: "ha salido", tipo: TipoMensaje.Sistema);

        // Act
        _origen.OnNext(CrearMensaje());
        _origen.OnNext(sistema1);
        _origen.OnNext(sistema2);

        // Assert
        _recibidos.Should().HaveCount(2);
        _recibidos.Should().AllSatisfy(m => m.Tipo.Should().Be(TipoMensaje.Sistema));
    }

    #endregion

    #region DeUsuario Tests

    [Test]
    public void DeUsuario_FlujoVariosAutores_SoloLosDeEseUsuario()
    {
        // Arrange
        _origen.DeUsuario("Ana").Subscribe(_recibidos.Add);
        var deAna = CrearMensaje(usuario: "Ana");

        // Act
        _origen.OnNext(deAna);
        _origen.OnNext(CrearMensaje(usuario: "Luis"));
        _origen.OnNext(CrearMensaje(usuario: "Marta"));

        // Assert
        _recibidos.Should().ContainSingle().Which.Should().Be(deAna);
    }

    [Test]
    public void DeUsuario_EsInsensibleAMayusculas()
    {
        // Arrange
        _origen.DeUsuario("ANA").Subscribe(_recibidos.Add);
        var deAna = CrearMensaje(usuario: "Ana");

        // Act
        _origen.OnNext(deAna);

        // Assert
        _recibidos.Should().ContainSingle();
    }

    #endregion

    #region FormatearLinea Tests

    [Test]
    public void FormatearLinea_MensajeNormal_RetornaLineaConHoraYAutor()
    {
        // Arrange
        string? linea = null;
        _origen.FormatearLinea().Subscribe(l => linea = l);

        // Act
        _origen.OnNext(CrearMensaje(usuario: "Ana", texto: "Buenas"));

        // Assert
        linea.Should().Be("[10:30] Ana: Buenas");
    }

    [Test]
    public void FormatearLinea_MensajeSistema_MarcaElPunto()
    {
        // Arrange
        string? linea = null;
        _origen.FormatearLinea().Subscribe(l => linea = l);

        // Act
        _origen.OnNext(CrearMensaje(usuario: "sistema", texto: "Ana se ha unido al canal", tipo: TipoMensaje.Sistema));

        // Assert
        linea.Should().Be("[10:30] · Ana se ha unido al canal");
    }

    #endregion

    #region AgruparPorUsuario Tests

    [Test]
    public void AgruparPorUsuario_FlujoVariosAutores_UnGrupoPorAutor()
    {
        // Arrange
        var grupos = new Dictionary<string, List<MensajeDto>>();
        _origen.AgruparPorUsuario().Subscribe(grupo =>
        {
            grupos[grupo.Key] = [];
            grupo.Subscribe(m => grupos[grupo.Key].Add(m));
        });

        // Act
        _origen.OnNext(CrearMensaje(usuario: "Ana"));
        _origen.OnNext(CrearMensaje(usuario: "Luis"));
        _origen.OnNext(CrearMensaje(usuario: "Ana", texto: "¿alguien más?"));

        // Assert
        grupos.Should().HaveCount(2);
        grupos["Ana"].Should().HaveCount(2);
        grupos["Luis"].Should().HaveCount(1);
    }

    #endregion

    #region Pipeline compuesto Tests

    [Test]
    public void Pipeline_FiltrarYFormatear_SeComponenEnCadena()
    {
        // Arrange: ejemplo didáctico del README — filtrar normales y formatear
        string? linea = null;
        _origen
            .SoloNormales()
            .FormatearLinea()
            .Subscribe(l => linea = l);

        // Act: primero un mensaje de sistema (que NO debe llegar) y luego uno normal
        _origen.OnNext(CrearMensaje(texto: "se ha unido", tipo: TipoMensaje.Sistema));
        _origen.OnNext(CrearMensaje(usuario: "Ana", texto: "Buenas a todos"));

        // Assert
        linea.Should().Be("[10:30] Ana: Buenas a todos");
    }

    #endregion
}
