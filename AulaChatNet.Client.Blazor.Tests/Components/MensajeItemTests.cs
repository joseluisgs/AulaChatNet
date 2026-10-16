using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace AulaChatNet.Client.Blazor.Tests.Components;

/// <summary>
/// Pruebas de una fila de mensaje: nombre en su color, hora LOCAL (el
/// servidor guarda UTC) y el formato de sistema (gris cursiva, sin avatar).
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class MensajeItemTests : BunitContext
{
    [SetUp]
    public void SetUp() => Services.AddSingleton(new ChatConfig());

    [Test]
    public void MensajeNormal_PintaNombreColorHoraYTexto()
    {
        // Arrange: 10:11 UTC — la conversión a hora local la hace el componente
        var momento = new DateTime(2026, 10, 15, 10, 11, 0, DateTimeKind.Utc);
        var mensaje = new MensajeDto(Guid.NewGuid(), "Ana", "Hola a todos", momento, TipoMensaje.Normal);

        // Act
        var cut = Render<MensajeItem>(p => p.Add(m => m.Mensaje, mensaje));

        // Assert
        cut.Find(".mensaje-nombre").TextContent.Should().Be("Ana");
        cut.Find(".mensaje-nombre").GetAttribute("style").Should().Contain("color: rgb(");
        cut.Find(".mensaje-hora").TextContent.Should().Be(momento.ToLocalTime().ToString("HH:mm"));
        cut.Find(".mensaje-texto").TextContent.Should().Be("Hola a todos");
    }

    [Test]
    public void MensajeNormal_LlevaSuAvatar()
    {
        // Arrange
        var mensaje = new MensajeDto(Guid.NewGuid(), "Ana", "Hola", DateTime.UtcNow, TipoMensaje.Normal);

        // Act
        var cut = Render<MensajeItem>(p => p.Add(m => m.Mensaje, mensaje));

        // Assert
        cut.FindAll("img.avatar").Should().HaveCount(1);
    }

    [Test]
    public void MensajeDeSistema_SinAvatar_yConPuntoDelanteDelTexto()
    {
        // Arrange
        var mensaje = new MensajeDto(
            Guid.NewGuid(), MensajesSistema.Autor, "Luis se ha unido al canal.",
            DateTime.UtcNow, TipoMensaje.Sistema);

        // Act
        var cut = Render<MensajeItem>(p => p.Add(m => m.Mensaje, mensaje));

        // Assert: mismo formato que la terminal: "HH:mm · texto"
        cut.Find(".mensaje").ClassList.Should().Contain("sistema");
        cut.FindAll("img").Should().BeEmpty();
        cut.Find(".mensaje-texto").TextContent.Should().Be("· Luis se ha unido al canal.");
    }

    [Test]
    public void MensajeDeSistema_TambienLlevaHora()
    {
        // Arrange: en un aula se quiere saber CUÁNDO se fue alguien
        var momento = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var mensaje = new MensajeDto(Guid.NewGuid(), MensajesSistema.Autor, "adiós", momento, TipoMensaje.Sistema);

        // Act
        var cut = Render<MensajeItem>(p => p.Add(m => m.Mensaje, mensaje));

        // Assert
        cut.Find(".mensaje-hora").TextContent.Should().Be(momento.ToLocalTime().ToString("HH:mm"));
    }
}
