using AulaChatNet.Client.Web.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AulaChatNet.Client.Web.Tests.Components;

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

    [Test]
    public void ClicEnUnCompanero_PidePrepararSuPrivado()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        store.Nick.OnNext("Ana");
        store.Usuarios.OnNext(
        [
            new UsuarioConectadoDto("Ana", DateTime.UtcNow),
            new UsuarioConectadoDto("Luis", DateTime.UtcNow)
        ]);
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());
        var cut = Render<ListaUsuarios>();

        // Act: el segundo de la lista es Luis
        cut.FindAll(".usuario")[1].Click();

        // Assert: los componentes no se hablan entre sí, piden a la store
        store.Mock.Verify(s => s.PrepararPrivado("Luis"), Times.Once);
    }

    [Test]
    public void ClicEnMiPropioNombre_NoPideNada()
    {
        // Arrange: un privado a ti mismo no tiene sentido, y el servidor lo
        // rechazaría — mejor no abrir siquiera la puerta
        var store = StoreFalsa.Crear();
        store.Nick.OnNext("Ana");
        store.Usuarios.OnNext([new UsuarioConectadoDto("Ana", DateTime.UtcNow)]);
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());
        var cut = Render<ListaUsuarios>();

        // Act
        cut.FindAll(".usuario")[0].Click();

        // Assert
        store.Mock.Verify(s => s.PrepararPrivado(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void MismoNombreConOtrasMayusculas_SigueSiendoElMismo()
    {
        // Arrange: el servidor normaliza al registrar, y aquí no debe
        // ofrecerse un privado a uno mismo aunque el nombre venga distinto
        var store = StoreFalsa.Crear();
        store.Nick.OnNext("Ana");
        store.Usuarios.OnNext([new UsuarioConectadoDto("ANA", DateTime.UtcNow)]);
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());
        var cut = Render<ListaUsuarios>();

        // Act
        cut.FindAll(".usuario")[0].Click();

        // Assert
        store.Mock.Verify(s => s.PrepararPrivado(It.IsAny<string>()), Times.Never);
    }
}
