using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AulaChatNet.Client.Blazor.Tests.Components;

/// <summary>
/// Pruebas de la zona de mensajes de la web: el canal sigue pintándose como
/// siempre, y los privados se MEZCLAN en su cronología en lugar de vivir en
/// una lista aparte — que es justo lo que comprueban los tests de orden.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ListaMensajesTests : BunitContext
{
    /// <summary>Momentos de prueba: 10:00, 11:00 y 12:00 UTC de un mismo día.</summary>
    private static readonly DateTime Diez = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Once = Diez.AddHours(1);
    private static readonly DateTime Doce = Diez.AddHours(2);

    /// <summary>Crea un mensaje del canal.</summary>
    private static MensajeDto Mensaje(string texto, DateTime momento) =>
        new(Guid.NewGuid(), "Ana", texto, momento, TipoMensaje.Normal);

    /// <summary>Crea un mensaje privado.</summary>
    private static MensajePrivadoDto Privado(string texto, DateTime momento) =>
        new(Guid.NewGuid(), "Ana", "Luis", texto, momento);

    /// <summary>Renderiza la lista con la store dada y el scroll de JS simulado.</summary>
    private IRenderedComponent<ListaMensajes> Renderizar(StoreFalsa store)
    {
        Services.AddSingleton(store.Objeto);
        Services.AddSingleton(new ChatConfig());

        // Bajar el scroll al llegar algo nuevo es JS del navegador (js/aulachat.js)
        JSInterop.SetupVoid("aulachat.abajo", _ => true);

        return Render<ListaMensajes>();
    }

    [Test]
    public void SinNada_MuestraElAvisoDeCanalVacio()
    {
        // Arrange
        var cut = Renderizar(StoreFalsa.Crear());

        // Assert
        cut.Find(".mensajes-vacio").TextContent.Should().Contain("Sé el primero");
    }

    [Test]
    public void MensajesDelCanal_SePintanComoSiempre()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        var cut = Renderizar(store);

        // Act
        store.Mensajes.OnNext([Mensaje("Buenas a todos", Diez)]);

        // Assert
        cut.FindAll(".mensaje").Should().HaveCount(1);
        cut.FindAll(".mensaje.privado").Should().BeEmpty();
    }

    [Test]
    public void LlegaUnPrivado_SePintaComoPrivadoYNoComoMensajeNormal()
    {
        // Arrange: soy Ana, así que su propio privado dice "para Luis"
        var store = StoreFalsa.Crear();
        store.Nick.OnNext("Ana");
        var cut = Renderizar(store);

        // Act
        store.Privados.OnNext([Privado("Confidencia", Once)]);

        // Assert: el 🔒 y el contexto "para Luis" lo pinta PrivadoItem
        cut.FindAll(".mensaje.privado").Should().HaveCount(1);
        cut.Find(".privado-marca").TextContent.Should().Contain("para Luis");
    }

    [Test]
    public void CanalYPrivados_SeOrdenanPorFecha()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        var cut = Renderizar(store);

        // Act: el canal trae 10:00 y 12:00; el privado, 11:00. Concatenarlos
        // tal cual dejaría el privado por debajo del mensaje de las 12:00
        store.Mensajes.OnNext([Mensaje("De las diez", Diez), Mensaje("De las doce", Doce)]);
        store.Privados.OnNext([Privado("De las once", Once)]);

        // Assert
        var filas = cut.FindAll(".mensajes > *").Select(f => f.TextContent).ToArray();

        filas.Should().HaveCount(3);
        filas[0].Should().Contain("De las diez");
        filas[1].Should().Contain("De las once").And.Contain("🔒");
        filas[2].Should().Contain("De las doce");
    }

    [Test]
    public void CambiarDeCanal_NoMezclaElHistorialViejoConElNuevo()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        var cut = Renderizar(store);
        store.Mensajes.OnNext([Mensaje("Mensaje del canal viejo", Diez)]);

        // Act: el servidor REEMPLAZA la vista al cambiar de canal
        store.Mensajes.OnNext([Mensaje("Mensaje del canal nuevo", Doce)]);

        // Assert
        var filas = cut.FindAll(".mensajes > *").Select(f => f.TextContent).ToArray();
        filas.Should().ContainSingle();
        filas[0].Should().Contain("canal nuevo").And.NotContain("canal viejo");
    }
}
