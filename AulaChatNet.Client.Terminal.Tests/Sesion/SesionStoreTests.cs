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
