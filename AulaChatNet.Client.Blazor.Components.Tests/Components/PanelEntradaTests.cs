using System.Net;
using System.Text;
using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Client.Blazor.Components.Configuracion;
using AulaChatNet.Client.Blazor.Components.State;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using AngleSharp.Html.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Moq;

namespace AulaChatNet.Client.Blazor.Components.Tests.Components;

/// <summary>
/// Pruebas del panel de entrada: la bienvenida con la gente conectada
/// (GET /estadisticas), la sesión recordada y — lo más delicado — el
/// comportamiento ante rechazos del servidor y fallos de red (que NO vuelva
/// a preguntar el nick y ofrezca reintentar).
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PanelEntradaTests : BunitContext
{
    /// <summary>Respuesta del servidor para /estadisticas en los tests.</summary>
    private const string EstadisticasJson =
        """
        {"totalConectados":3,"canales":[{"canal":"#general","conectados":2},{"canal":"#daw","conectados":1}]}
        """;

    private StoreFalsa _store = null!;
    private Mock<ISesionStore> _sesion = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear();
        _sesion = new Mock<ISesionStore>();
        _sesion.Setup(s => s.CargarAsync()).ReturnsAsync((SesionGuardada?)null);
        _sesion.Setup(s => s.GuardarAsync(It.IsAny<SesionGuardada>())).Returns(Task.CompletedTask);
        _sesion.Setup(s => s.BorrarAsync()).ReturnsAsync(false);

        Services.AddSingleton(_store.Objeto);
        Services.AddSingleton(_sesion.Object);
        Services.AddSingleton(new ChatConfig
        {
            Canales = ["#general", "#daw"],
            UsuarioLongitudMinima = 2,
            UsuarioLongitudMaxima = 20,
            MensajeLongitudMaxima = 500
        });
        Services.AddSingleton(new WebConfig());
        ServidorConGente();
    }

    [Test]
    public void MuestraLaGenteConectadaAntesDeEntrar()
    {
        // Arrange y act: ya se registró el HttpClient en el SetUp
        var cut = Render<PanelEntrada>();

        // Assert: total y reparto por sala, en el orden de la configuración
        cut.Find(".entrada-stats").TextContent.Should().Contain(
            "📌 Actualmente hay 3 personas conectadas: #general 2 · #daw 1");
    }

    [Test]
    public void SinNadieConectado_LoDiceConSuFrase()
    {
        // Arrange
        ServidorConGente("""{"totalConectados":0,"canales":[]}""");

        // Act
        var cut = Render<PanelEntrada>();

        // Assert
        cut.Find(".entrada-stats").TextContent.Should().Contain("no hay nadie conectado todavía");
    }

    [Test]
    public void ServidorCaido_MuestraAvisoSinTiposDeExcepcion()
    {
        // Arrange: el GET falla (sin red, apagado...)
        ServidorQueFalla();

        // Act
        var cut = Render<PanelEntrada>();

        // Assert: la cifra es opcional; el arranque NO se corta
        cut.Find(".entrada-stats.aviso").TextContent.Should()
            .Contain("No pude consultar cuántos hay conectados")
            .And.NotContain("Exception");
    }

    [Test]
    public void SesionRecordada_DejaNickCanalYEnlaceDeBorrarlo()
    {
        // Arrange: alguien ya entró desde este navegador
        _sesion.Setup(s => s.CargarAsync()).ReturnsAsync(new SesionGuardada("Ana", "#daw"));

        // Act
        var cut = Render<PanelEntrada>();

        // Assert
        ((IHtmlInputElement)cut.Find("input#nick")).Value.Should().Be("Ana");
        ((IHtmlSelectElement)cut.Find("select#canal")).Value.Should().Be("#daw");
        cut.Find(".entrada-sesion").TextContent.Should().Contain("Hola de nuevo");

        // Y el enlace de borrar funciona (equipos compartidos de aula)
        cut.Find(".entrada-sesion a").Click();
        _sesion.Verify(s => s.BorrarAsync(), Times.Once);
    }

    [Test]
    public void NickDemasiadoCorto_MuestraElErrorSinLlamarAlServidor()
    {
        // Arrange
        var cut = Render<PanelEntrada>();
        cut.Find("input#nick").Input("A");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: validación local = cero idas y vueltas
        cut.Find(".error").TextContent.Should().Contain(
            "El nombre de usuario debe tener entre 2 y 20 caracteres");
        _store.Mock.Verify(
            s => s.ConectarAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void EntrarCorrecto_ConectaYGuardaLaSesion()
    {
        // Arrange
        _store.Mock.Setup(s => s.ConectarAsync("Ana", "#general"))
            .ReturnsAsync(new ResultadoConexion(null, HayServidor: true));
        var cut = Render<PanelEntrada>();
        cut.Find("input#nick").Input("Ana");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert
        _store.Mock.Verify(s => s.ConectarAsync("Ana", "#general"), Times.Once);
        _sesion.Verify(s => s.GuardarAsync(
            It.Is<SesionGuardada>(x => x.Nick == "Ana" && x.Canal == "#general")), Times.Once);
    }

    [Test]
    public void RechazoDelServidor_MuestraElErrorYOfreceCorregirElNick()
    {
        // Arrange: el servidor respondió y dice que el nombre está en uso
        _store.Mock.Setup(s => s.ConectarAsync("Ana", "#general"))
            .ReturnsAsync(new ResultadoConexion("Ese nombre de usuario ya está en uso, elige otro", HayServidor: true));
        var cut = Render<PanelEntrada>();
        cut.Find("input#nick").Input("Ana");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: hay servidor → hay que tocar el nick; el botón sigue siendo "Entrar"
        cut.Find(".error").TextContent.Should().Contain("ya está en uso");
        cut.Find("button.boton").TextContent.Trim().Should().Be("Entrar");
        ((IHtmlInputElement)cut.Find("input#nick")).Value.Should().Be("Ana", "el formulario no se vacía");
    }

    [Test]
    public void FalloDeRed_ConservaElNickYOfreceReintentar()
    {
        // Arrange: el servidor ni se pudo alcanzar (el caso de la terminal
        // en el que NO hay que volver a preguntar el nick)
        _store.Mock.Setup(s => s.ConectarAsync("Ana", "#general"))
            .ReturnsAsync(new ResultadoConexion("No se pudo conectar con el servidor: no hay red.", HayServidor: false));
        var cut = Render<PanelEntrada>();
        cut.Find("input#nick").Input("Ana");

        // Act
        cut.Find("form").TriggerEvent("onsubmit", EventArgs.Empty);

        // Assert: solo se reintenta; el nick y el canal se conservan
        cut.Find("button.boton").TextContent.Trim().Should().Be("Reintentar");
        ((IHtmlInputElement)cut.Find("input#nick")).Value.Should().Be("Ana");
        cut.Find(".error").TextContent.Should().Contain("No se pudo conectar");
    }

    // ── Ayudantes ──

    /// <summary>Registra un HttpClient que responde /estadisticas con gente.</summary>
    private void ServidorConGente(string json = EstadisticasJson) =>
        Servidor(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    /// <summary>Registra un HttpClient cuya descarga lanza un fallo de red.</summary>
    private void ServidorQueFalla() =>
        Servidor(() => throw new HttpRequestException("sin conexión"));

    private void Servidor(Func<HttpResponseMessage> responder) =>
        Services.AddSingleton(new HttpClient(new HandlerFalso(responder))
        {
            BaseAddress = new Uri("http://localhost/")
        });

    /// <summary>Handler mínimo que responde (o falla) sin salir de la memoria.</summary>
    private sealed class HandlerFalso(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder());
    }
}
