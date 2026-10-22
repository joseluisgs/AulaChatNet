using AulaChatNet.Client.Web.Components.Shared;
using AulaChatNet.Shared.Dtos;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Moq;

namespace AulaChatNet.Client.Web.Tests.Components;

/// <summary>
/// Pruebas de las píldoras de canal: orden de la configuración, conteo por
/// sala (evento global), píldora activa y el camino de cambio de grupo —
/// incluida la regla de que los fallos de conexión van al flujo de mensajes.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ListaCanalesTests : BunitContext
{
    private StoreFalsa _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear(["#general", "#daw", "#dam"]);
        Services.AddSingleton(_store.Objeto);
    }

    [Test]
    public void Pildoras_EnElOrdenDeLaConfiguracionConSuConteo()
    {
        // Arrange: el servidor solo emite las salas CON GENTE; #dam sale sola con 0
        _store.Estadisticas.OnNext(new EstadisticasDto(5,
        [
            new ConteoCanalDto("#general", 2),
            new ConteoCanalDto("#daw", 3)
        ]));

        // Act
        var cut = Render<ListaCanales>();

        // Assert
        var pildoras = cut.FindAll("button.pildora");
        pildoras.Should().HaveCount(3);
        pildoras[0].TextContent.Should().StartWith("#general");
        pildoras[1].TextContent.Should().StartWith("#daw");
        pildoras[2].TextContent.Should().StartWith("#dam");
        cut.FindAll(".pildora-contador").Select(c => c.TextContent).Should().Equal("2", "3", "0");
    }

    [Test]
    public void PildoraActiva_SigueAlCanalDeLaStore()
    {
        // Arrange
        _store.CanalActual.OnNext("#daw");
        var cut = Render<ListaCanales>();

        // Act: cambio de canal en caliente
        _store.CanalActual.OnNext("#dam");

        // Assert
        var pildoras = cut.FindAll("button.pildora");
        pildoras[2].ClassList.Should().Contain("activa");
        pildoras[1].ClassList.Should().NotContain("activa");
    }

    [Test]
    public void PulsarUnaPildora_CambiaDeCanal()
    {
        // Arrange
        _store.CanalActual.OnNext("#general");
        _store.Mock.Setup(s => s.CambiarCanalAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        var cut = Render<ListaCanales>();

        // Act
        cut.FindAll("button.pildora")[2].Click();

        // Assert
        _store.Mock.Verify(s => s.CambiarCanalAsync("#dam"), Times.Once);
    }

    [Test]
    public void PulsarLaDelCanalActual_NoHaceNada()
    {
        // Arrange: ya estás en #general, no hay que re-enviar el alta
        _store.CanalActual.OnNext("#general");
        _store.Mock.Setup(s => s.CambiarCanalAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        var cut = Render<ListaCanales>();

        // Act
        cut.FindAll("button.pildora")[0].Click();

        // Assert
        _store.Mock.Verify(
            s => s.CambiarCanalAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void SiElCambioFalla_SeAvisaEnElFlujoDeMensajes()
    {
        // Arrange: la píldora es válida, pero la conexión está caída
        _store.CanalActual.OnNext("#general");
        _store.Mock.Setup(s => s.CambiarCanalAsync("#daw"))
            .ReturnsAsync("Sin conexión con el servidor");
        var cut = Render<ListaCanales>();

        // Act
        cut.FindAll("button.pildora")[1].Click();

        // Assert: regla de la casa — el error de conexión entra como aviso
        // de sistema en el flujo, no pegado en la píldora
        _store.Mock.Verify(s => s.Aviso("Sin conexión con el servidor"), Times.Once);
        cut.FindAll(".pildora .error").Should().BeEmpty();
    }
}
