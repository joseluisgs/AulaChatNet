using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AngleSharp.Html.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Moq;

namespace AulaChatNet.Client.Blazor.Tests.Components;

/// <summary>
/// Pruebas de la caja de texto: validación local junto al campo, envío a la
/// store y la regla de la casa — lo del CAMPO se pinta aquí (vacío, largo);
/// lo de CONEXIÓN va al flujo de mensajes como aviso.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CajaMensajeTests : BunitContext
{
    private StoreFalsa _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear();
        Services.AddSingleton(_store.Objeto);
        Services.AddSingleton(new ChatConfig
        {
            MensajeLongitudMaxima = 500
        });

        // Tras enviar, la caja devuelve el foco con JS puro (js/aulachat.js):
        // sin navegador no hay foco que dar, bUnit deja pasar esa llamada
        JSInterop.SetupVoid("aulachat.enfocar", _ => true);
    }

    [Test]
    public void Boton_Deshabilitado_CuandoElCampoEstaVacio()
    {
        // Arrange y act
        var cut = Render<CajaMensaje>();

        // Assert: sin texto no hay nada que enviar
        cut.Find("button.caja-boton").HasAttribute("disabled").Should().BeTrue();
    }

    [Test]
    public void Escribir_HabilitaElBoton()
    {
        // Arrange
        var cut = Render<CajaMensaje>();

        // Act
        cut.Find("input.caja-entrada").Input("hola");

        // Assert
        cut.Find("button.caja-boton").HasAttribute("disabled").Should().BeFalse();
    }

    [Test]
    public async Task EnviarTextoValido_LlamaALaStoreYBorraElCampo()
    {
        // Arrange
        _store.Mock.Setup(s => s.EnviarAsync("hola")).ReturnsAsync((string?)null);
        var cut = Render<CajaMensaje>();
        cut.Find("input.caja-entrada").Input("hola");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: el mensaje salió y el campo queda limpio para el siguiente
        _store.Mock.Verify(s => s.EnviarAsync("hola"), Times.Once);
        ((IHtmlInputElement)cut.Find("input.caja-entrada")).Value.Should().BeEmpty();
        cut.FindAll(".caja-aviso").Should().BeEmpty();
    }

    [Test]
    public void EnviarVacio_NoLlamaALaStoreYSeAvisa()
    {
        // Arrange
        var cut = Render<CajaMensaje>();

        // Act: se fuerza el envío igual (el botón ya vendrá deshabilitado)
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert
        _store.Mock.Verify(s => s.EnviarAsync(It.IsAny<string>()), Times.Never);
        cut.Find(".caja-aviso").TextContent.Should().Contain("no puede estar vacío");
    }

    [Test]
    public void EnviarDemasiadoLargo_NoLlamaALaStoreYSeAvisa()
    {
        // Arrange: la validación local es la MISMA que la del servidor
        var cut = Render<CajaMensaje>();
        cut.Find("input.caja-entrada").Input(new string('a', 501));

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert
        _store.Mock.Verify(s => s.EnviarAsync(It.IsAny<string>()), Times.Never);
        cut.Find(".caja-aviso").TextContent.Should().Contain("no puede superar los 500 caracteres");
    }

    [Test]
    public void SiFallaLaConexion_ElAvisoVaAlFlujoDeMensajes()
    {
        // Arrange: el campo está perfecto; lo que falla es la conexión
        _store.Mock.Setup(s => s.EnviarAsync("hola"))
            .ReturnsAsync("Sin conexión con el servidor");
        var cut = Render<CajaMensaje>();
        cut.Find("input.caja-entrada").Input("hola");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: regla de la casa — aviso en el flujo, campo sin error
        _store.Mock.Verify(s => s.Aviso("Sin conexión con el servidor"), Times.Once);
        cut.FindAll(".caja-aviso").Should().BeEmpty();
    }
}
