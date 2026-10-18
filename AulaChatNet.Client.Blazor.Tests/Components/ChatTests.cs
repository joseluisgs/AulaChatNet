using System.Net;
using System.Text;
using AulaChatNet.Client.Blazor.Components;
using AulaChatNet.Client.Blazor.Components.Pages;
using AulaChatNet.Client.Blazor.Configuracion;
using AulaChatNet.Client.Blazor.Services;
using AulaChatNet.Client.Blazor.State;
using AulaChatNet.Shared.Configuracion;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Moq;

namespace AulaChatNet.Client.Blazor.Tests.Components;

/// <summary>
/// Pruebas de la página conductora: su ÚNICA decisión es de dónde viene la
/// sesión — sin ella se pinta el panel de entrada, con ella la vista de
/// canal completa. El que manda es el observable EnSesion de la store.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ChatTests : BunitContext
{
    private StoreFalsa _store = null!;

    [SetUp]
    public void SetUp()
    {
        // El canal baja el scroll con JS puro (wwwroot/js/aulachat.js): sin
        // navegador no hay scroll que probar, bUnit lo deja pasar en suelto
        JSInterop.Mode = JSRuntimeMode.Loose;
        _store = StoreFalsa.Crear();

        var sesion = new Mock<ISesionStore>();
        sesion.Setup(s => s.CargarAsync()).ReturnsAsync((SesionGuardada?)null);

        Services.AddSingleton(_store.Objeto);
        Services.AddSingleton(sesion.Object);

        // La vista de canal incluye la caja de texto, que despacha los
        // comandos (/canal...) al procesador compartido
        Services.AddSingleton(Mock.Of<IProcesadorComandos>());
        Services.AddSingleton(new ChatConfig
        {
            Canales = ["#general", "#daw"]
        });
        Services.AddSingleton(new WebConfig());

        // El panel de entrada consulta la gente conectada al arrancar
        Services.AddSingleton(new HttpClient(new HandlerFalso())
        {
            BaseAddress = new Uri("http://localhost/")
        });
    }

    [Test]
    public void SinSesion_MuestraElPanelDeEntrada()
    {
        // Arrange: la store arranca con EnSesion = false (BehaviorSubject)

        // Act
        var cut = Render<Chat>();

        // Assert
        cut.FindAll(".entrada").Should().HaveCount(1);
        cut.FindAll(".contenido").Should().BeEmpty("aún no hay canal que mostrar");
    }

    [Test]
    public void ConSesion_MuestraLaVistaDeCanalaCompleta()
    {
        // Arrange: la store avisa de que el alta triunfó ANTES de pintar
        _store.EnSesion.OnNext(true);

        // Act
        var cut = Render<Chat>();

        // Assert: las cinco piezas del chat, y sin rastro de la entrada
        cut.FindAll(".barra").Should().HaveCount(1);
        cut.FindAll(".lista-usuarios").Should().HaveCount(1);
        cut.FindAll(".pildoras").Should().HaveCount(1);
        cut.FindAll(".mensajes").Should().HaveCount(1);
        cut.FindAll(".caja-envoltura").Should().HaveCount(1);
        cut.FindAll(".entrada").Should().BeEmpty();
    }

    [Test]
    public void AlPerderLaSesion_VuelveAlPanelDeEntrada()
    {
        // Arrange: dentro del chat
        _store.EnSesion.OnNext(true);
        var cut = Render<Chat>();

        // Act: el botón Salir (vía store) cierra la sesión
        _store.EnSesion.OnNext(false);

        // Assert: la misma página cambia de mitad sin recargar
        cut.FindAll(".entrada").Should().HaveCount(1);
        cut.FindAll(".contenido").Should().BeEmpty();
    }

    /// <summary>Handler mínimo: el panel solo pide /estadisticas al arrancar.</summary>
    private sealed class HandlerFalso : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"totalConectados":0,"canales":[]}""",
                    Encoding.UTF8, "application/json")
            });
    }
}
