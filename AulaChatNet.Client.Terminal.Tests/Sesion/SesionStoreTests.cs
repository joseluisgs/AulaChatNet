using AulaChatNet.Client.Terminal.Sesion;
using FluentAssertions;

namespace AulaChatNet.Client.Terminal.Tests.Sesion;

/// <summary>
/// Suite de tests para SesionStore: persistencia del nick y canal recordados.
/// Cada test usa una carpeta temporal propia (guid) y la borra al terminar,
/// así no se ensucia el equipo real ni se pisan los tests entre sí.
/// </summary>
[TestFixture]
public class SesionStoreTests
{
    private string _directorio = null!;
    private string _ruta = null!;
    private SesionStore _store = null!;

    [SetUp]
    public void Setup()
    {
        // Carpeta temporal única por test
        _directorio = Path.Combine(Path.GetTempPath(), "aulachat-tests", Guid.NewGuid().ToString("N"));
        _ruta = Path.Combine(_directorio, "sesion.json");
        _store = new SesionStore(_ruta);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directorio))
            Directory.Delete(_directorio, recursive: true);
    }

    [Test]
    public void Cargar_ArchivoInexistente_DevuelveNull()
    {
        // Act: primera ejecución del chat en este equipo
        var sesion = _store.Cargar();

        // Assert
        sesion.Should().BeNull();
    }

    [Test]
    public void Guardar_Cargar_DevuelveLaMismaSesion()
    {
        // Arrange
        var original = new SesionTerminal("Ana", "#daw");

        // Act
        _store.Guardar(original);
        var cargada = _store.Cargar();

        // Assert: roundtrip completo nick + canal
        cargada.Should().Be(original);
    }

    [Test]
    public void Guardar_CargarConservaLaFechaDeUltimaConexion()
    {
        // Arrange
        var cuando = new DateTime(2026, 10, 14, 18, 30, 0);
        var original = new SesionTerminal("Ana", "#daw", cuando);

        // Act
        _store.Guardar(original);
        var cargada = _store.Cargar();

        // Assert: la fecha alimenta la bienvenida ("... el 14/10/2026 18:30")
        cargada.Should().Be(original);
        cargada!.UltimaConexion.Should().Be(cuando);
    }

    [Test]
    public void Cargar_FicheroAntiguoSinFecha_LaCargaIgual()
    {
        // Arrange: sesión guardada por una versión anterior (sin el campo fecha)
        Directory.CreateDirectory(_directorio);
        File.WriteAllText(_ruta, """{ "Nick": "Ana", "Canal": "#general" }""");

        // Act
        var sesion = _store.Cargar();

        // Assert: se carga igual; solo la bienvenida omite la fecha
        sesion.Should().NotBeNull();
        sesion!.Nick.Should().Be("Ana");
        sesion.UltimaConexion.Should().Be(default);
    }

    [Test]
    public void Guardar_DosVeces_SobrescribeLaAnterior()
    {
        // Arrange
        _store.Guardar(new SesionTerminal("Ana", "#daw"));

        // Act: cambia de nick y de canal
        _store.Guardar(new SesionTerminal("Luis", "#general"));
        var cargada = _store.Cargar();

        // Assert
        cargada.Should().Be(new SesionTerminal("Luis", "#general"));
    }

    [Test]
    public void Guardar_CarpetaInexistente_LaCrea()
    {
        // Arrange: la ruta apunta a una subcarpeta que no existe aún
        var rutaProfunda = Path.Combine(_directorio, "a", "b", "sesion.json");
        var store = new SesionStore(rutaProfunda);

        // Act
        store.Guardar(new SesionTerminal("Ana", "#general"));

        // Assert
        File.Exists(rutaProfunda).Should().BeTrue();
        store.Cargar().Should().Be(new SesionTerminal("Ana", "#general"));
    }

    [Test]
    public void Cargar_ArchivoCorrupto_DevuelveNullSinLanzar()
    {
        // Arrange: un fichero que no es JSON (alguien lo editó a mano mal)
        Directory.CreateDirectory(_directorio);
        File.WriteAllText(_ruta, "no es json {{{");

        // Act
        var sesion = _store.Cargar();

        // Assert: nunca lanza; simplemente no hay sesión y se vuelve a preguntar
        sesion.Should().BeNull();

        // ...y además queda MARCADO para que la bienienda diga
        // "fichero corrupto" en vez de fingir que es la primera vez
        _store.SesionCorrupta.Should().BeTrue();
    }

    [Test]
    public void Cargar_SinFichero_NoMarcaSesionCorrupta()
    {
        // Arrange: no existe ningún sesion.json (caso primera vez)

        // Act
        var sesion = _store.Cargar();

        // Assert: sin fichero NO es lo mismo que fichero roto
        sesion.Should().BeNull();
        _store.SesionCorrupta.Should().BeFalse();
    }

    [Test]
    public void Cargar_SesionValida_NoMarcaSesionCorrupta()
    {
        // Arrange
        _store.Guardar(new SesionTerminal("Ana", "#general", new DateTime(2026, 10, 15, 12, 0, 0)));

        // Act
        var sesion = _store.Cargar();

        // Assert: una carga correcta deja el marcador limpio
        sesion.Should().NotBeNull();
        _store.SesionCorrupta.Should().BeFalse();
    }

    [Test]
    public void RutaFichero_InstanciaPorDefecto_EstaEnElPerfilDelUsuario()
    {
        // Act: sin ruta inyectada, usa %APPDATA%\AulaChatNet\sesion.json
        var storePorDefecto = new SesionStore();

        // Assert
        storePorDefecto.RutaFichero.Should().Contain("AulaChatNet")
            .And.EndWith("sesion.json");
    }
}
