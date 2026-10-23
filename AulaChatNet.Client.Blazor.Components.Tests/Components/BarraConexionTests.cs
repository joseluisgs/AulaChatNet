using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Client.Blazor.Components.State;
using AulaChatNet.Shared.Dtos;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Moq;

namespace AulaChatNet.Client.Blazor.Components.Tests.Components;

/// <summary>
/// Pruebas de la barra superior: identidad, punto de estado (la misma paleta
/// que la terminal), conteos (sala + total) y los dos botones — Reconectar
/// (solo cuando la conexión se ha caído) y Salir.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class BarraConexionTests : BunitContext
{
    private StoreFalsa _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear();
        _store.Nick.OnNext("Ana");
        _store.CanalActual.OnNext("#general");
        _store.Usuarios.OnNext([new UsuarioConectadoDto("Ana", DateTime.UtcNow)]);
        _store.Estadisticas.OnNext(new EstadisticasDto(3, []));
        _store.Mock.Setup(s => s.CerrarAsync()).Returns(Task.CompletedTask);

        Services.AddSingleton(_store.Objeto);
    }

    [Test]
    public void ConConexionEstable_MuestraIdentidadPuntoYConteos()
    {
        // Arrange
        _store.Estado.OnNext(EstadoConexion.Conectado);

        // Act
        var cut = Render<BarraConexion>();

        // Assert: "1 en la sala" (evento de grupo) · "3 en total" (evento global)
        cut.Find(".barra-nick").TextContent.Should().Be("Ana");
        cut.Find(".barra-canal").TextContent.Should().Be("#general");
        cut.Find(".punto.conectado").Should().NotBeNull();
        cut.Find(".barra-contador").TextContent.Should()
            .Contain("1 en la sala").And.Contain("3 en total");
        cut.FindAll("button.boton-secundario").Select(b => b.TextContent.Trim())
            .Should().NotContain("Reconectar", "con la conexión viva no se ofrece");
    }

    [Test]
    public void Reconectando_PoneElPuntoNaranja()
    {
        // Arrange
        _store.Estado.OnNext(EstadoConexion.Reconectando);

        // Act
        var cut = Render<BarraConexion>();

        // Assert
        cut.Find(".punto.reconectando").Should().NotBeNull();
        cut.Find(".barra").TextContent.Should().Contain("reconectando");
    }

    [Test]
    public void ConexionCaida_MuestraElBotonReconectar()
    {
        // Arrange
        _store.Estado.OnNext(EstadoConexion.Desconectado);

        // Act
        var cut = Render<BarraConexion>();

        // Assert
        cut.Find(".punto.desconectado").Should().NotBeNull();
        cut.FindAll("button.boton-secundario").Select(b => b.TextContent.Trim())
            .Should().Contain("Reconectar");
    }

    [Test]
    public void Reconectar_LlamaALaStoreY_MuestraElErrorTraducido()
    {
        // Arrange: la reconexión manual devuelve un error ya traducido
        _store.Estado.OnNext(EstadoConexion.Desconectado);
        _store.Mock.Setup(s => s.ReconectarAsync())
            .ReturnsAsync("No se pudo reconectar: no hay red.");
        var cut = Render<BarraConexion>();

        // Act
        cut.FindAll("button.boton-secundario")
            .Single(b => b.TextContent.Trim() == "Reconectar").Click();

        // Assert
        _store.Mock.Verify(s => s.ReconectarAsync(), Times.Once);
        cut.Find(".barra-error").TextContent.Should().Contain("no hay red");
    }

    [Test]
    public void Salir_CierraLaSesionEnLaStore()
    {
        // Arrange
        var cut = Render<BarraConexion>();

        // Act: la store pondrá EnSesion a false y la página volverá al panel
        cut.FindAll("button.boton-secundario")
            .Single(b => b.TextContent.Trim() == "Salir").Click();

        // Assert
        _store.Mock.Verify(s => s.CerrarAsync(), Times.Once);
    }
}
