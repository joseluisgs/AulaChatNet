using System.Reactive.Subjects;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Observables;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Observables;

/// <summary>
///     Suite de tests para los operadores Rx de MensajeObservableExtensions.
///     Usa un Subject como origen simulado del flujo de mensajes del hub:
///     Subject emite de forma síncrona, así los tests no necesitan esperas.
/// </summary>
[TestFixture]
public class MensajeObservableExtensionsTests {
    [SetUp]
    public void Setup() {
        _origen = new Subject<MensajeDto>();
        _recibidos = [];
    }

    [TearDown]
    public void TearDown() {
        _origen.Dispose();
    }

    private Subject<MensajeDto> _origen = null!;
    private List<MensajeDto> _recibidos = null!;

    /// <summary>Crea un mensaje de prueba con los valores dados.</summary>
    private static MensajeDto CrearMensaje(
        string usuario = "Ana",
        string texto = "hola",
        TipoMensaje tipo = TipoMensaje.Normal) {
        return new MensajeDto(Guid.NewGuid(), usuario, texto, new DateTime(2026, 1, 1, 10, 30, 0, DateTimeKind.Utc),
            tipo);
    }

    [Test]
    public void SoloNormales_FlujoMixto_FiltraLosMensajesDeSistema() {
        // Arrange
        _origen.SoloNormales().Subscribe(_recibidos.Add);
        MensajeDto normal = CrearMensaje(texto: "Buenas");
        MensajeDto sistema = CrearMensaje(tipo: TipoMensaje.Sistema);

        // Act
        _origen.OnNext(normal);
        _origen.OnNext(sistema);

        // Assert
        _recibidos.Should().ContainSingle().Which.Should().Be(normal);
    }

    [Test]
    public void SoloSistema_FlujoMixto_FiltraLosMensajesNormales() {
        // Arrange
        _origen.SoloSistema().Subscribe(_recibidos.Add);
        MensajeDto sistema1 = CrearMensaje(tipo: TipoMensaje.Sistema);
        MensajeDto sistema2 = CrearMensaje("Luis", "ha salido", TipoMensaje.Sistema);

        // Act
        _origen.OnNext(CrearMensaje());
        _origen.OnNext(sistema1);
        _origen.OnNext(sistema2);

        // Assert
        _recibidos.Should().HaveCount(2);
        _recibidos.Should().AllSatisfy(m => m.Tipo.Should().Be(TipoMensaje.Sistema));
    }

    [Test]
    public void DeUsuario_FlujoVariosAutores_SoloLosDeEseUsuario() {
        // Arrange
        _origen.DeUsuario("Ana").Subscribe(_recibidos.Add);
        MensajeDto deAna = CrearMensaje();

        // Act
        _origen.OnNext(deAna);
        _origen.OnNext(CrearMensaje("Luis"));
        _origen.OnNext(CrearMensaje("Marta"));

        // Assert
        _recibidos.Should().ContainSingle().Which.Should().Be(deAna);
    }

    [Test]
    public void DeUsuario_EsInsensibleAMayusculas() {
        // Arrange
        _origen.DeUsuario("ANA").Subscribe(_recibidos.Add);
        MensajeDto deAna = CrearMensaje();

        // Act
        _origen.OnNext(deAna);

        // Assert
        _recibidos.Should().ContainSingle();
    }

    [Test]
    public void FormatearLinea_MensajeNormal_RetornaLineaConHoraYAutor() {
        // Arrange
        string? linea = null;
        _origen.FormatearLinea().Subscribe(l => linea = l);

        // Act
        _origen.OnNext(CrearMensaje("Ana", "Buenas"));

        // Assert
        linea.Should().Be("[10:30] Ana: Buenas");
    }

    [Test]
    public void FormatearLinea_MensajeSistema_MarcaElPunto() {
        // Arrange
        string? linea = null;
        _origen.FormatearLinea().Subscribe(l => linea = l);

        // Act
        _origen.OnNext(CrearMensaje("sistema", "Ana se ha unido al canal", TipoMensaje.Sistema));

        // Assert
        linea.Should().Be("[10:30] · Ana se ha unido al canal");
    }

    [Test]
    public void AgruparPorUsuario_FlujoVariosAutores_UnGrupoPorAutor() {
        // Arrange
        var grupos = new Dictionary<string, List<MensajeDto>>();
        _origen.AgruparPorUsuario().Subscribe(grupo => {
            grupos[grupo.Key] = [];
            grupo.Subscribe(m => grupos[grupo.Key].Add(m));
        });

        // Act
        _origen.OnNext(CrearMensaje());
        _origen.OnNext(CrearMensaje("Luis"));
        _origen.OnNext(CrearMensaje("Ana", "¿alguien más?"));

        // Assert
        grupos.Should().HaveCount(2);
        grupos["Ana"].Should().HaveCount(2);
        grupos["Luis"].Should().HaveCount(1);
    }

    [Test]
    public void Pipeline_FiltrarYFormatear_SeComponenEnCadena() {
        // Arrange: ejemplo didáctico del README — filtrar normales y formatear
        string? linea = null;
        _origen
            .SoloNormales()
            .FormatearLinea()
            .Subscribe(l => linea = l);

        // Act: primero un mensaje de sistema (que NO debe llegar) y luego uno normal
        _origen.OnNext(CrearMensaje(texto: "se ha unido", tipo: TipoMensaje.Sistema));
        _origen.OnNext(CrearMensaje("Ana", "Buenas a todos"));

        // Assert
        linea.Should().Be("[10:30] Ana: Buenas a todos");
    }
}
