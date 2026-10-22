using AulaChatNet.Client.Web.Components.Shared;
using AulaChatNet.Client.Web.State;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AulaChatNet.Client.Web.Tests.Components;

/// <summary>
/// Pruebas del badge de menciones (@ana): se OCULTA con el contador a cero,
/// se muestra con el contador lleno y el clic da las menciones por vistas.
/// El parpadeo de la pestaña vive en JavaScript —se prueba en ejecución,
/// no aquí— y el resaltado del texto, en <see cref="TextoMencion"/>.
/// </summary>
// Sin este atributo, NUnit reutiliza el MISMO BunitContext para toda la
// fixture: el primer test materializa el proveedor de servicios y los
// siguientes no ya pueden registrar los suyos
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AvisoMencionesTests : BunitContext
{
    private StoreFalsa _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear();
        Services.AddSingleton(_store.Objeto);

        // El clic del badge apaga el parpadeo de la pestaña con una función
        // JS: bUnit va en modo estricto y exige declarar cada llamada, y el
        // manejador, su resultado (si no, la espera se quedaría colgada)
        JSInterop.SetupVoid("aulachat.terminarParpadeo").SetVoidResult();
    }

    [Test]
    public void SinMenciones_NoPintaNada()
    {
        // Act
        var cut = Render<AvisoMenciones>();

        // Assert: el badge solo asoma cuando hay algo que ver
        cut.FindAll("button.mencion-aviso").Should().BeEmpty();
    }

    [Test]
    public void ConMencionesSinMirar_MuestraElContador()
    {
        // Arrange: el flujo lo menea el test, igual que haría el evento
        // del servidor en la store de verdad
        _store.MencionesNoVistas.OnNext(2);

        // Act
        var cut = Render<AvisoMenciones>();

        // Assert
        cut.Find("button.mencion-aviso").TextContent.Should().Contain("2");
    }

    [Test]
    public void PulsarElBadge_DaLasMencionesPorVistas()
    {
        // Arrange
        _store.MencionesNoVistas.OnNext(3);
        var cut = Render<AvisoMenciones>();

        // Act
        cut.Find("button.mencion-aviso").Click();

        // Assert: el clic llega a la store —ella es quien pone el contador a
        // cero; el componente no lo toca por su cuenta. El handler es async
        // (apaga además el parpadeo de la pestaña), así que se espera a que
        // termine en vez de mirar al vuelo
        cut.WaitForAssertion(
            () => _store.Mock.Verify(s => s.MarcarMencionesVistas(), Times.Once));
    }
}
