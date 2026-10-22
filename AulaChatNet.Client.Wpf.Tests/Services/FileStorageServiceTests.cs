using AulaChatNet.Client.Web.State;
using AulaChatNet.Client.Wpf.Services;

namespace AulaChatNet.Client.Wpf.Tests.Services;

/// <summary>
/// Tests del almacenamiento local del cliente de escritorio: la MISMA
/// abstracción que el localStorage del navegador (<c>ILocalStorageService</c>),
/// pero guardando en un fichero JSON. Cada test trabaja en una carpeta
/// temporal: el servicio real guarda en %LOCALAPPDATA% y los tests no deben
/// tocar el equipo de quien los ejecuta.
/// </summary>
[TestFixture]
public class FileStorageServiceTests
{
    #region Configuración de prueba

    /// <summary>Carpeta temporal de cada test.</summary>
    private string _carpeta = null!;

    /// <summary>Servicio bajo prueba, apuntando a la carpeta temporal.</summary>
    private FileStorageService _almacenamiento = null!;

    [SetUp]
    public void SetUp()
    {
        // Arrange compartido: carpeta temporal NUEVA por test
        _carpeta = Path.Combine(Path.GetTempPath(), $"aulachat-storage-{Guid.NewGuid():N}");
        _almacenamiento = new FileStorageService(carpeta: _carpeta);
    }

    [TearDown]
    public void TearDown()
    {
        // Quitar los restos: el siguiente test arranca con carpeta limpia
        if (Directory.Exists(_carpeta))
            Directory.Delete(_carpeta, true);
    }

    #endregion

    #region GetItemAsync

    [Test]
    public async Task GetItemAsync_SinNadaGuardado_DevuelveNull()
    {
        // Arrange (hecho en el SetUp: carpeta vacía, ni fichero)

        // Act
        var sesion = await _almacenamiento.GetItemAsync<SesionGuardada>("aulachat.sesion");

        // Assert: primer arranque = "no hay sesión", sin lanzar nada
        Assert.That(sesion, Is.Null);
    }

    [Test]
    public async Task SetItemAsync_LuegoGetItemAsync_DevuelveElMismoObjeto()
    {
        // Arrange
        var original = new SesionGuardada("Link", "#general");

        // Act
        await _almacenamiento.SetItemAsync("aulachat.sesion", original);
        var recuperada = await _almacenamiento.GetItemAsync<SesionGuardada>("aulachat.sesion");

        // Assert: los records comparan por valor — roundtrip perfecto
        Assert.That(recuperada, Is.EqualTo(original));
    }

    [Test]
    public async Task SetItemAsync_ConDosClavesDistintas_LasGuardaLasDos()
    {
        // Arrange y Act
        await _almacenamiento.SetItemAsync("primera", new SesionGuardada("Link", "#general"));
        await _almacenamiento.SetItemAsync("segunda", new SesionGuardada("Momo", "#daw"));

        var primera = await _almacenamiento.GetItemAsync<SesionGuardada>("primera");
        var segunda = await _almacenamiento.GetItemAsync<SesionGuardada>("segunda");

        // Assert: como el localStorage, cada clave vive a su lado
        Assert.Multiple(() =>
        {
            Assert.That(primera!.Nick, Is.EqualTo("Link"));
            Assert.That(segunda!.Nick, Is.EqualTo("Momo"));
        });
    }

    [Test]
    public async Task GetItemAsync_ConFicheroCorrupto_DevuelveNullSinLanzar()
    {
        // Arrange: el fichero existe pero NO es JSON (alguien lo ha tocado a mano)
        Directory.CreateDirectory(Path.Combine(_carpeta, "AulaChatNet"));
        File.WriteAllText(
            Path.Combine(_carpeta, "AulaChatNet", "almacenamiento.json"),
            "esto no es json {{");

        // Act
        var sesion = await _almacenamiento.GetItemAsync<SesionGuardada>("aulachat.sesion");

        // Assert: ni lanza ni devuelve basura — la app sigue como sin sesión
        Assert.That(sesion, Is.Null);
    }

    [Test]
    public async Task GetItemAsync_ConValorDeOtraVersion_DevuelveNullSinLanzar()
    {
        // Arrange: clave válida pero con un valor que ya no tiene esa forma
        Directory.CreateDirectory(Path.Combine(_carpeta, "AulaChatNet"));
        File.WriteAllText(
            Path.Combine(_carpeta, "AulaChatNet", "almacenamiento.json"),
            """{ "aulachat.sesion": 12345 }""");

        // Act
        var sesion = await _almacenamiento.GetItemAsync<SesionGuardada>("aulachat.sesion");

        // Assert: mismo tratamiento que en la web: "no hay nada legible"
        Assert.That(sesion, Is.Null);
    }

    #endregion

    #region RemoveItemAsync y ClearAsync

    [Test]
    public async Task RemoveItemAsync_ConClaveExistente_LaBorraYConservaLaOtra()
    {
        // Arrange
        await _almacenamiento.SetItemAsync("primera", new SesionGuardada("Link", "#general"));
        await _almacenamiento.SetItemAsync("segunda", new SesionGuardada("Momo", "#daw"));

        // Act
        await _almacenamiento.RemoveItemAsync("primera");

        // Assert
        Assert.That(await _almacenamiento.GetItemAsync<SesionGuardada>("primera"), Is.Null);
        Assert.That(await _almacenamiento.GetItemAsync<SesionGuardada>("segunda"), Is.Not.Null);
    }

    [Test]
    public async Task ClearAsync_ConVariasClaves_DejaElAlmacenamientoVacio()
    {
        // Arrange
        await _almacenamiento.SetItemAsync("primera", new SesionGuardada("Link", "#general"));
        await _almacenamiento.SetItemAsync("segunda", new SesionGuardada("Momo", "#daw"));

        // Act
        await _almacenamiento.ClearAsync();

        // Assert
        Assert.That(await _almacenamiento.GetItemAsync<SesionGuardada>("primera"), Is.Null);
        Assert.That(await _almacenamiento.GetItemAsync<SesionGuardada>("segunda"), Is.Null);
    }

    #endregion

    #region Varias instancias

    [Test]
    public async Task DosInstancias_CompartenElMismoFichero()
    {
        // Arrange: otra instancia con la MISMA carpeta (p. ej. una segunda
        // ventana abierta del mismo equipo: el fichero es el mismo)
        var otra = new FileStorageService(carpeta: _carpeta);

        // Act
        await _almacenamiento.SetItemAsync("aulachat.sesion", new SesionGuardada("Link", "#general"));

        // Assert
        Assert.That(await otra.GetItemAsync<SesionGuardada>("aulachat.sesion"), Is.Not.Null);
    }

    #endregion
}
