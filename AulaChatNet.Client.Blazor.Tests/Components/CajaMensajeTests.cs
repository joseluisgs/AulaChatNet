using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Client.Blazor.Services;
using AulaChatNet.Shared.Comandos;
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
/// lo de CONEXIÓN va al flujo de mensajes como aviso. Y lo que empieza por
/// "/" se despacha al procesador de comandos (mismo parser que la terminal)
/// en lugar de ir al canal.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CajaMensajeTests : BunitContext
{
    private StoreFalsa _store = null!;
    private Mock<IProcesadorComandos> _procesador = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear();
        _procesador = new Mock<IProcesadorComandos>();
        Services.AddSingleton(_store.Objeto);
        Services.AddSingleton(_procesador.Object);
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

    [Test]
    public async Task EnviarComando_LoDespachaAlProcesadorYNoAlCanal()
    {
        // Arrange: una línea con "/" es un COMANDO (parser de Shared), no un mensaje
        var cut = Render<CajaMensaje>();
        cut.Find("input.caja-entrada").Input("/canal daw");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: el comando salió hacia el procesador; al canal, ni hablar.
        // (Se captura la invocación y se afirma con BeOfType: los árboles de
        // expresión de Moq no admiten el operador "is" de los patrones)
        _procesador.Invocations.Should().HaveCount(1);
        _procesador.Invocations[0].Arguments[0]
            .Should().BeOfType<Comando.Canal>()
            .Which.Nombre.Should().Be("daw");
        _store.Mock.Verify(s => s.EnviarAsync(It.IsAny<string>()), Times.Never);
        ((IHtmlInputElement)cut.Find("input.caja-entrada")).Value.Should().BeEmpty();
    }

    [Test]
    public async Task ComandoDesconocido_NoSeEnviaAlCanal()
    {
        // Arrange
        var cut = Render<CajaMensaje>();
        cut.Find("input.caja-entrada").Input("/magia");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: el parser lo tipa como desconocido y va al procesador,
        // que es quien lo comunica en el flujo (nunca como mensaje del canal)
        _procesador.Invocations.Should().HaveCount(1);
        _procesador.Invocations[0].Arguments[0]
            .Should().BeOfType<Comando.Desconocido>()
            .Which.Palabra.Should().Be("/magia");
        _store.Mock.Verify(s => s.EnviarAsync(It.IsAny<string>()), Times.Never);
    }

    #region Privado Tests

    [Test]
    public void PrivadoCompleto_MuestraElChipConElDestinatarioYLaNota()
    {
        // Arrange
        var cut = Render<CajaMensaje>();

        // Act: con destino Y texto, la línea ya es un privado
        cut.Find("input.caja-entrada").Input("/privado Ana ¿nos vemos?");

        // Assert: el chip nombra a la persona y la nota avisa de lo importante
        cut.Find(".chip-privado").TextContent.Should().Contain("Ana");
        cut.Find(".caja-nota").TextContent.Should().Contain("no se guardan");
    }

    [Test]
    public void PrivadoSinDestino_TodaviaNoHayChip_SiSaleLaNota()
    {
        // Arrange: mientras se teclea el destino, aún no hay a quién nombrar,
        // pero el aviso de que no se guarda ya puede estar
        var cut = Render<CajaMensaje>();

        // Act
        cut.Find("input.caja-entrada").Input("/privado ");

        // Assert
        cut.FindAll(".chip-privado").Should().BeEmpty();
        cut.Find(".caja-nota").TextContent.Should().Contain("no se guardan");
    }

    [Test]
    public void LineaNormal_NoMuestraNadaDePrivado()
    {
        // Arrange
        var cut = Render<CajaMensaje>();

        // Act: la charla normal no debe llevar ni chip ni nota
        cut.Find("input.caja-entrada").Input("buenas a todos");

        // Assert
        cut.FindAll(".chip-privado").Should().BeEmpty();
        cut.FindAll(".caja-nota").Should().BeEmpty();
    }

    [Test]
    public void ClicEnLaLista_PreRellenaLaLineaConElPrivado()
    {
        // Arrange
        var cut = Render<CajaMensaje>();

        // Act: la lista de conectados pide escribirle a Luis
        _store.DestinatarioPrivado.OnNext("Luis");

        // Assert: queda puesto el comando, con el espacio final para seguir escribiendo
        ((IHtmlInputElement)cut.Find("input.caja-entrada")).Value.Should().Be("/privado Luis ");
        cut.Find(".chip-privado").TextContent.Should().Contain("Luis");
    }

    [Test]
    public void CerrarElChip_BorraLaLineaYVuelveElPlaceholder()
    {
        // Arrange
        var cut = Render<CajaMensaje>();
        _store.DestinatarioPrivado.OnNext("Luis");

        // Act
        cut.Find(".chip-cerrar").Click();

        // Assert
        cut.FindAll(".chip-privado").Should().BeEmpty();
        cut.FindAll(".caja-nota").Should().BeEmpty();
        ((IHtmlInputElement)cut.Find("input.caja-entrada")).Value.Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivado_LoDespachaAlProcesadorYNoAlCanal()
    {
        // Arrange
        var cut = Render<CajaMensaje>();
        cut.Find("input.caja-entrada").Input("/privado Ana hola");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: mismo camino que cualquier otro comando del parser
        _procesador.Invocations.Should().HaveCount(1);
        _procesador.Invocations[0].Arguments[0]
            .Should().BeOfType<Comando.Privado>()
            .Which.Destinatario.Should().Be("Ana");
        _store.Mock.Verify(s => s.EnviarAsync(It.IsAny<string>()), Times.Never);

        // Y como la línea ya se fue, el chip y la nota desaparecen
        cut.FindAll(".chip-privado").Should().BeEmpty();
        cut.FindAll(".caja-nota").Should().BeEmpty();
    }

    #endregion
}
