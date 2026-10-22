using AulaChatNet.Client.Web.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace AulaChatNet.Client.Web.Tests.Components;

/// <summary>
/// Pruebas del avatar con respaldo: la imagen de DiceBear y, si el navegador
/// no puede cargarla (sin internet o API caída), las iniciales con el color
/// determinista. El fallo se simula disparando el evento onerror a mano.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AvatarTests : BunitContext
{
    [SetUp]
    public void SetUp() => Services.AddSingleton(new ChatConfig
    {
        AvatarBaseUrl = "https://api.dicebear.com/10.x"
    });

    [Test]
    public void ImagenDeDiceBear_UsaElNombreComoSemilla()
    {
        // Arrange y act
        var cut = Render<Avatar>(p => p.Add(a => a.Usuario, "Ana"));

        // Assert: misma semilla → mismo avatar para siempre
        var img = cut.Find("img");
        img.GetAttribute("src").Should()
            .StartWith("https://api.dicebear.com/10.x/")
            .And.Contain("seed=Ana");
        img.GetAttribute("class").Should().Contain("avatar");
    }

    [Test]
    public void CuandoLaImagenFalla_PintaInicialesConColorDeFondo()
    {
        // Arrange
        var cut = Render<Avatar>(p =>
        {
            p.Add(a => a.Usuario, "Ana");
            p.Add(a => a.Tamano, 40);
        });

        // Act: el navegador nos avisa del fallo (sin red, DiceBear caído...)
        cut.Find("img").TriggerEvent("onerror", EventArgs.Empty);

        // Assert: círculo con iniciales (sin corchetes: ellos son de la
        // terminal) y el color determinista del usuario
        var circulo = cut.Find("span.avatar-iniciales");
        circulo.TextContent.Should().Be("AN");
        circulo.GetAttribute("style").Should()
            .Contain("width: 40px")
            .And.Contain("background-color: rgb(");
    }

    [Test]
    public void TrasElFallo_NoVuelveAPedirLaImagen()
    {
        // Arrange
        var cut = Render<Avatar>(p => p.Add(a => a.Usuario, "Ana"));

        // Act
        cut.Find("img").TriggerEvent("onerror", EventArgs.Empty);

        // Assert
        cut.FindAll("img").Should().BeEmpty("una vez fallado, manda el respaldo");
    }

    [Test]
    public void UsuarioDeUnaPalabra_MuestraDosLetras()
    {
        // Arrange y act
        var cut = Render<Avatar>(p => p.Add(a => a.Usuario, "Luis"));

        // Act
        cut.Find("img").TriggerEvent("onerror", EventArgs.Empty);

        // Assert
        cut.Find("span.avatar-iniciales").TextContent.Should().Be("LU");
    }
}
