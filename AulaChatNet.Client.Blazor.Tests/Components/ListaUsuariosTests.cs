using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace AulaChatNet.Client.Blazor.Tests.Components;

/// <summary>
/// Pruebas de la columna de conectados: SOLO los del canal actual (evento de
/// grupo) con su punto verde; el total GLOBAL vive en la barra, no aquí.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ListaUsuariosTests : BunitContext
{
    [Test]
    public void PintaUnUsuarioPorCadaConectadoDeLaSala()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        store.Usuarios.OnNext(
        [
            new UsuarioConectadoDto("Ana", DateTime.UtcNow),
            new UsuarioConectadoDto("Luis", DateTime.UtcNow)
        ]);
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());

        // Act
        var cut = Render<ListaUsuarios>();

        // Assert: fila + avatar + nombre + punto verde por persona
        cut.FindAll(".usuario").Should().HaveCount(2);
        cut.FindAll(".usuario .avatar, .usuario .avatar-iniciales").Should().HaveCount(2);
        cut.FindAll(".usuario .punto.conectado").Should().HaveCount(2);
        cut.FindAll(".usuario-nombre").Select(n => n.TextContent).Should().Equal("Ana", "Luis");
    }

    [Test]
    public void Titulo_MuestraElConteoDeLaSala()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        store.Usuarios.OnNext([new UsuarioConectadoDto("Ana", DateTime.UtcNow)]);
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());

        // Act
        var cut = Render<ListaUsuarios>();

        // Assert
        cut.Find(".lista-titulo").TextContent.Should().Contain("Conectados").And.Contain("1");
    }

    [Test]
    public void ListaVacia_DejaUnAvisoAmable()
    {
        // Arrange: nadie más en la sala (de momento)
        var store = StoreFalsa.Crear();
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());

        // Act
        var cut = Render<ListaUsuarios>();

        // Assert
        cut.FindAll(".usuario").Should().BeEmpty();
        cut.Find(".lista-vacio").TextContent.Should().Contain("Solo estás tú");
    }

    [Test]
    public void CuandoLlegaAlguienNuevo_SeRefrescaSinVolverAPintar()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());
        var cut = Render<ListaUsuarios>();

        // Act: el servidor avisa de que entra alguien (evento de grupo)
        store.Usuarios.OnNext([new UsuarioConectadoDto("María", DateTime.UtcNow)]);

        // Assert: la suscripción hizo su trabajo y el repintado llegó
        cut.FindAll(".usuario").Should().HaveCount(1);
        cut.Find(".usuario-nombre").TextContent.Should().Be("María");
    }
}
