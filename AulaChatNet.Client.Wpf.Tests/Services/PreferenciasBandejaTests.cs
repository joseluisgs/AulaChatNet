using AulaChatNet.Client.Blazor.Components.Services.Storage;   // ILocalStorageService
using AulaChatNet.Client.Wpf.Services;           // PreferenciasBandeja
using Moq;

namespace AulaChatNet.Client.Wpf.Tests.Services;

/// <summary>
/// Tests de <see cref="PreferenciasBandeja"/>: la lógica que decide si el
/// aviso de "la app sigue en segundo plano" se enseña o no. Es lógica pura
/// (WPF no entra), así que se prueba con un doble de su almacenamiento.
/// </summary>
[TestFixture]
public class PreferenciasBandejaTests
{
    #region EsPrimeraVezAsync

    [Test]
    public async Task EsPrimeraVezAsync_SinAvisoPrevio_DevuelveTrue()
    {
        // Arrange: almacenamiento vacío (la clave no existe → default(bool))
        var almacen = new Mock<ILocalStorageService>();
        almacen.Setup(a => a.GetItemAsync<bool>(PreferenciasBandeja.ClaveAvisoMostrado))
            .ReturnsAsync(false);
        var preferencias = new PreferenciasBandeja(almacen.Object);

        // Act
        var resultado = await preferencias.EsPrimeraVezAsync();

        // Assert: la primera vez hay que enseñar el aviso
        Assert.That(resultado, Is.True);
    }

    [Test]
    public async Task EsPrimeraVezAsync_ConAvisoYaEnsenado_DevuelveFalse()
    {
        // Arrange: el usuario ya vio el aviso en una sesión anterior
        var almacen = new Mock<ILocalStorageService>();
        almacen.Setup(a => a.GetItemAsync<bool>(PreferenciasBandeja.ClaveAvisoMostrado))
            .ReturnsAsync(true);
        var preferencias = new PreferenciasBandeja(almacen.Object);

        // Act
        var resultado = await preferencias.EsPrimeraVezAsync();

        // Assert: no se vuelve a molestar con la burbuja
        Assert.That(resultado, Is.False);
    }

    #endregion

    #region MarcarAvisoMostradoAsync

    [Test]
    public async Task MarcarAvisoMostradoAsync_EscribeLaClaveEnElAlmacenamiento()
    {
        // Arrange
        var almacen = new Mock<ILocalStorageService>();
        var preferencias = new PreferenciasBandeja(almacen.Object);

        // Act
        await preferencias.MarcarAvisoMostradoAsync();

        // Assert: la marca queda guardada con valor true en su clave
        almacen.Verify(a => a.SetItemAsync(PreferenciasBandeja.ClaveAvisoMostrado, true),
            Times.Once);
    }

    #endregion
}
