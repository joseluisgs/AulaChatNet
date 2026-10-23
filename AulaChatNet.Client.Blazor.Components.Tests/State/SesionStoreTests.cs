using AulaChatNet.Client.Blazor.Components.Services.Storage;
using AulaChatNet.Client.Blazor.Components.State;
using FluentAssertions;
using Microsoft.JSInterop;
using Moq;

namespace AulaChatNet.Client.Blazor.Components.Tests.State;

/// <summary>
/// Pruebas de la sesión del navegador sobre la abstracción
/// <see cref="ILocalStorageService"/> (mismo patrón que la Tienda): sin
/// navegador y sin JS real — la store solo decide la política: qué se
/// guarda, qué se borra y qué pasa si el navegador no deja escribir.
/// </summary>
public class SesionStoreTests
{
    private const string Clave = "aulachat.sesion";

    private Mock<ILocalStorageService> _almacenamiento = null!;
    private SesionStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _almacenamiento = new Mock<ILocalStorageService>();
        _store = new SesionStore(_almacenamiento.Object);
    }

    [Test]
    public async Task CargarAsync_SinNadaGuardado_DevuelveNull()
    {
        // Arrange
        _almacenamiento.Setup(a => a.GetItemAsync<SesionGuardada>(Clave))
            .ReturnsAsync((SesionGuardada?)null);

        // Act
        var sesion = await _store.CargarAsync();

        // Assert
        sesion.Should().BeNull();
    }

    [Test]
    public async Task CargarAsync_ConLoQueHaya_DevuelveLaSesion()
    {
        // Arrange
        _almacenamiento.Setup(a => a.GetItemAsync<SesionGuardada>(Clave))
            .ReturnsAsync(new SesionGuardada("Ana", "#daw"));

        // Act
        var sesion = await _store.CargarAsync();

        // Assert
        sesion.Should().NotBeNull();
        sesion!.Nick.Should().Be("Ana");
        sesion.Canal.Should().Be("#daw");
    }

    [Test]
    public async Task CargarAsync_SinAlmacenamiento_DevuelveNullSinLanzar()
    {
        // Arrange: navegador en modo privado → JSException al acceder
        _almacenamiento.Setup(a => a.GetItemAsync<SesionGuardada>(Clave))
            .ThrowsAsync(new JSException("storage disabled"));

        // Act
        var sesion = await _store.CargarAsync();

        // Assert: no hay sesión y el chat no se cae
        sesion.Should().BeNull();
    }

    [Test]
    public async Task GuardarAsync_EnviaNickYCanalALaClave()
    {
        // Act
        await _store.GuardarAsync(new SesionGuardada("Ana", "#general"));

        // Assert
        _almacenamiento.Verify(a => a.SetItemAsync(
            Clave, It.Is<SesionGuardada>(s => s.Nick == "Ana" && s.Canal == "#general")),
            Times.Once);
    }

    [Test]
    public async Task GuardarAsync_SinAlmacenamiento_NoLanza()
    {
        // Arrange: grabar en modo privado no debe tumbar la entrada al chat
        _almacenamiento.Setup(a => a.SetItemAsync<SesionGuardada>(Clave, It.IsAny<SesionGuardada>()))
            .ThrowsAsync(new JSException("storage disabled"));

        // Act
        Func<Task> act = async () => await _store.GuardarAsync(new SesionGuardada("Ana", null));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task BorrarAsync_HabiendoSesion_DevuelveTrueYQuitaLaClave()
    {
        // Arrange
        _almacenamiento.Setup(a => a.GetItemAsync<SesionGuardada>(Clave))
            .ReturnsAsync(new SesionGuardada("Ana", null));

        // Act
        var borrada = await _store.BorrarAsync();

        // Assert
        borrada.Should().BeTrue();
        _almacenamiento.Verify(a => a.RemoveItemAsync(Clave), Times.Once);
    }

    [Test]
    public async Task BorrarAsync_SinSesion_DevuelveFalse()
    {
        // Arrange
        _almacenamiento.Setup(a => a.GetItemAsync<SesionGuardada>(Clave))
            .ReturnsAsync((SesionGuardada?)null);

        // Act
        var borrada = await _store.BorrarAsync();

        // Assert: borrar algo que no existe no es un error, es "no había nada"
        borrada.Should().BeFalse();
        _almacenamiento.Verify(a => a.RemoveItemAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task BorrarAsync_SinAlmacenamiento_DevuelveFalseSinLanzar()
    {
        // Arrange
        _almacenamiento.Setup(a => a.GetItemAsync<SesionGuardada>(Clave))
            .ThrowsAsync(new JSException("storage disabled"));

        // Act
        var borrada = await _store.BorrarAsync();

        // Assert
        borrada.Should().BeFalse();
    }
}
