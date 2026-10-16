using AulaChatNet.Client.Blazor.Services.Storage;
using AulaChatNet.Client.Blazor.State;
using FluentAssertions;
using Microsoft.JSInterop;
using Moq;

namespace AulaChatNet.Client.Blazor.Tests.Services.Storage;

/// <summary>
/// Pruebas del servicio de almacenamiento (calco del de la Tienda): JSON
/// dentro, navegador fuera. Se mockea el <c>IJSRuntime</c> con Moq.
///
/// <remarks>
/// Detalle del camino real: <c>InvokeVoidAsync</c> NO es mockeable (su
/// resultado <c>IJSVoidResult</c> es interno), pero SÍ llega al proxy como
/// <c>InvokeAsync(identifier, args)</c> — así que las ESCRITURAS se
/// verifican por su identificador en el registro de invocaciones, y las
/// LECTURAS (que sí devuelven <c>string?</c>) con un Setup normal.
/// </remarks>
/// </summary>
public class LocalStorageServiceTests
{
    private const string Clave = "aulachat.sesion";

    private Mock<IJSRuntime> _js = null!;
    private LocalStorageService _servicio = null!;

    [SetUp]
    public void SetUp()
    {
        _js = new Mock<IJSRuntime>();
        _servicio = new LocalStorageService(_js.Object);
    }

    [Test]
    [TestCase("""{"Nick":"Ana","Canal":"#daw"}""", "Ana", "#daw")]
    [TestCase("""{"nick":"Ana"}""", "Ana", null)] // indiferente a mayúsculas
    public async Task GetItem_ConJsonValido_DevuelveElObjeto(string json, string nick, string? canal)
    {
        // Arrange
        _js.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync(json);

        // Act
        var sesion = await _servicio.GetItemAsync<SesionGuardada>(Clave);

        // Assert
        sesion.Should().NotBeNull();
        sesion!.Nick.Should().Be(nick);
        sesion.Canal.Should().Be(canal);
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    public async Task GetItem_SinElemento_DevuelveNull(string? json)
    {
        // Arrange
        _js.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync(json);

        // Act
        var sesion = await _servicio.GetItemAsync<SesionGuardada>(Clave);

        // Assert
        sesion.Should().BeNull();
    }

    [Test]
    public async Task GetItem_ConJsonIlegible_DevuelveNullSinLanzar()
    {
        // Arrange: JSON tocado a mano o de otra versión
        _js.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync("{esto no es json");

        // Act
        var sesion = await _servicio.GetItemAsync<SesionGuardada>(Clave);

        // Assert: "no hay nada" en vez de excepción
        sesion.Should().BeNull();
    }

    [Test]
    public async Task SetItem_EscribeElJsonEnLocalStorage()
    {
        // Act: el mock suelto deja completar la llamada y la registra
        await _servicio.SetItemAsync(Clave, new SesionGuardada("Ana", "#general"));

        // Assert: la escritura llega como InvokeAsync("localStorage.setItem", [clave, json])
        var invocacion = EscriturasDe("localStorage.setItem");
        var argumentos = (object?[])invocacion.Arguments[1]!;

        argumentos[0].Should().Be(Clave);
        argumentos[1]!.ToString().Should().Contain("Ana").And.Contain("#general");
    }

    [Test]
    public async Task RemoveItem_BorraLaClaveDada()
    {
        // Act
        await _servicio.RemoveItemAsync(Clave);

        // Assert
        var invocacion = EscriturasDe("localStorage.removeItem");
        var argumentos = (object?[])invocacion.Arguments[1]!;

        argumentos[0].Should().Be(Clave);
    }

    [Test]
    public async Task Clear_LimpiaTodoElAlmacenamiento()
    {
        // Act
        await _servicio.ClearAsync();

        // Assert: basta con que exista la invocación con ese identificador
        EscriturasDe("localStorage.clear");
    }

    // ── Ayudante ──

    /// <summary>
    /// Busca la invocación de escritura (el InvokeVoidAsync acaba llegando al
    /// proxy como InvokeAsync) con ese identificador; si no existe, el test
    /// falla aquí con el detalle de las llamadas que sí hubo.
    /// </summary>
    private IInvocation EscriturasDe(string identificador) =>
        _js.Invocations.Single(i =>
            i.Method.Name == "InvokeAsync" &&
            (string?)i.Arguments[0] == identificador);
}
