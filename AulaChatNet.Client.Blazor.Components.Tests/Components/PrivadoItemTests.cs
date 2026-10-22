using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AulaChatNet.Client.Blazor.Components.Tests.Components;

/// <summary>
/// Pruebas de la fila de mensaje privado: MISMA estructura que un mensaje
/// normal, con el 🔒 rosa, el "para/de quien" según quién lo escribió y el
/// aviso de que no se guarda colgado del title del candado.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PrivadoItemTests : BunitContext
{
    /// <summary>Crea el privado de prueba: de Ana a Luis.</summary>
    private static MensajePrivadoDto Privado() =>
        new(Guid.NewGuid(), "Ana", "Luis", "Esto no lo ve nadie más",
            new DateTime(2026, 3, 2, 11, 0, 0, DateTimeKind.Utc));

    /// <summary>Renderiza la fila con el nick dado como "yo".</summary>
    private IRenderedComponent<PrivadoItem> Renderizar(string? yo)
    {
        var store = StoreFalsa.Crear();
        store.Nick.OnNext(yo);
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());

        return Render<PrivadoItem>(p => p.Add(x => x.Privado, Privado()));
    }

    [Test]
    public void PrivadoQueEscribiYo_DiceParaQuienEs()
    {
        // Arrange y act
        var cut = Renderizar("Ana");

        // Assert
        cut.Find(".privado-marca").TextContent.Should().Contain("para Luis");
    }

    [Test]
    public void PrivadoQueMeEnviaron_DiceQuienLoEscribio()
    {
        // Arrange y act: yo soy Luis, así que el de Ana es "de Ana"
        var cut = Renderizar("Luis");

        // Assert
        cut.Find(".privado-marca").TextContent.Should().Contain("de Ana");
    }

    [Test]
    public void Marca_LlevaLaNotaDeQueNoSeGuardaEnElTitle()
    {
        // Arrange y act
        var cut = Renderizar("Luis");

        // Assert: descubrible al pasar el ratón, sin sumar una línea al flujo
        cut.Find(".privado-marca")
            .GetAttribute("title").Should().Contain("no se guardan");
    }

    [Test]
    public void Fila_LlevaElCuerpoDelMensaje()
    {
        // Arrange y act
        var cut = Renderizar("Luis");

        // Assert: debajo del candado sigue estando el mensaje, igual que siempre
        cut.Find(".mensaje-texto").TextContent.Should().Be("Esto no lo ve nadie más");
    }
}
