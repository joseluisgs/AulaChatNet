using AulaChatNet.Client.Web.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AulaChatNet.Client.Web.Tests.Components;

/// <summary>
/// Pruebas del componente de reacciones: los chips que se pintan bajo el
/// mensaje, el "+" que abre el selector con la lista cerrada y la llamada
/// al servidor al elegir un emoji. La store es la falsa: aquí lo que importa
/// es QUÉ se le pide, no cómo llega el eco (ese camino lo prueba el
/// servidor con sus pruebas de red).
/// </summary>
[NUnit.Framework.FixtureLifeCycle(NUnit.Framework.LifeCycle.InstancePerTestCase)]
public class ReaccionesMensajeTests : BunitContext
{
    private StoreFalsa _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear();
        Services.AddSingleton(_store.Objeto);
    }

    /// <summary>Un chip de prueba: el emoji dado con las personas dadas.</summary>
    private static ReaccionDto Chip(string emoji, params string[] personas) =>
        new(emoji, personas);

    [Test]
    public void SinReacciones_NoPintaNingunChip()
    {
        // Act
        var cut = Render<ReaccionesMensaje>(p => p.Add(c => c.Reacciones, new List<ReaccionDto>()));

        // Assert: el "+" de la fila sí está (siempre se puede reaccionar),
        // pero sin chips no hay nada que contar
        cut.FindAll(".chip-reaccion").Should().BeEmpty();
        cut.FindAll(".boton-reaccion").Should().ContainSingle();
    }

    [Test]
    public void ConReacciones_PintaUnChipPorEmojiConSuContador()
    {
        // Arrange
        var chips = new List<ReaccionDto> { Chip("👍", "Ana", "Bruno"), Chip("😂", "Luis") };

        // Act
        var cut = Render<ReaccionesMensaje>(p => p.Add(c => c.Reacciones, chips));

        // Assert: dos chips, y en el tuyo el contador es el de personas
        var pintados = cut.FindAll(".chip-reaccion");
        pintados.Should().HaveCount(2);
        pintados[0].TextContent.Should().Contain("2");
        pintados[1].TextContent.Should().Contain("1");
    }

    [Test]
    public void ChipConTuNombre_LlevaLaClaseDeResaltado()
    {
        // Arrange: quien mira es Ana, y el chip lo pusieron Ana y Bruno
        var chips = new List<ReaccionDto> { Chip("👍", "Ana", "Bruno") };

        // Act
        var cut = Render<ReaccionesMensaje>(p => p
            .Add(c => c.Reacciones, chips)
            .Add(c => c.Nick, "Ana"));

        // Assert
        cut.Find(".chip-reaccion").ClassList.Should().Contain("mia");
    }

    [Test]
    public void ChipSoloAjeno_NoSeResalta()
    {
        // Arrange: Ana mira, pero el chip es de Bruno
        var chips = new List<ReaccionDto> { Chip("👍", "Bruno") };

        // Act
        var cut = Render<ReaccionesMensaje>(p => p
            .Add(c => c.Reacciones, chips)
            .Add(c => c.Nick, "Ana"));

        // Assert
        cut.Find(".chip-reaccion").ClassList.Should().NotContain("mia");
    }

    [Test]
    public void Selector_AntesDePulsarMas_NoSeVe()
    {
        // Act
        var cut = Render<ReaccionesMensaje>(p => p.Add(c => c.Reacciones, new List<ReaccionDto>()));

        // Assert
        cut.FindAll(".selector-reacciones").Should().BeEmpty();
    }

    [Test]
    public void PulsarMas_AbreElSelectorConLosSeisEmojiDeLaLista()
    {
        // Act: el "+" es la única puerta al selector (en la app, del hover)
        var cut = Render<ReaccionesMensaje>(p => p.Add(c => c.Reacciones, new List<ReaccionDto>()));
        cut.Find(".boton-reaccion").Click();

        // Assert: EXACTAMENTE la lista cerrada de Shared, en su orden — si
        // un emoji se cuela aquí, el servidor lo rechazaría después
        var opciones = cut.FindAll(".opcion-reaccion");
        opciones.Select(o => o.TextContent).Should().Equal(EmojisReaccion.Admitidos);
    }

    [Test]
    public void ElegirUnEmoji_InvocaAlServerConElIdDelMensajeYCierraElSelector()
    {
        // Arrange
        var idMensaje = Guid.NewGuid();
        var cut = Render<ReaccionesMensaje>(p => p
            .Add(c => c.IdMensaje, idMensaje)
            .Add(c => c.Reacciones, new List<ReaccionDto>()));
        cut.Find(".boton-reaccion").Click();

        // Act: el tercer emoji de la lista (😂)
        cut.FindAll(".opcion-reaccion")[2].Click();

        // Assert: se invoca con el ID de ESTE mensaje, y el selector se cierra
        _store.Mock.Verify(s => s.ReaccionarAsync(idMensaje, "😂"), Times.Once);
        cut.FindAll(".selector-reacciones").Should().BeEmpty();
    }

    [Test]
    public void PulsarUnChip_EsElMismoInterruptorQueElSelector()
    {
        // Arrange: el chip ya existe con 👍 de Ana
        var idMensaje = Guid.NewGuid();
        var chips = new List<ReaccionDto> { Chip("👍", "Ana") };
        var cut = Render<ReaccionesMensaje>(p => p
            .Add(c => c.IdMensaje, idMensaje)
            .Add(c => c.Reacciones, chips)
            .Add(c => c.Nick, "Ana"));

        // Act: pulsar EL PROPIO chip es quitarse, no duplicarse
        cut.Find(".chip-reaccion").Click();

        // Assert: el servidor decide el alterna; el cliente solo invoca
        _store.Mock.Verify(s => s.ReaccionarAsync(idMensaje, "👍"), Times.Once);
    }

    [Test]
    public void SiElServerRechaza_SeAvisaEnLaVistaDeMensajes()
    {
        // Arrange: el servidor rechaza (p. ej. el mensaje ya salió de la caché)
        _store.Mock.Setup(s => s.ReaccionarAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync("Ese mensaje ya no está en el canal");

        var cut = Render<ReaccionesMensaje>(p => p
            .Add(c => c.IdMensaje, Guid.NewGuid())
            .Add(c => c.Reacciones, new List<ReaccionDto>()));

        // Act: se abre el selector y se elige el primero de la lista
        cut.Find(".boton-reaccion").Click();
        cut.FindAll(".opcion-reaccion")[0].Click();

        // Assert: el error va como aviso por el MISMO flujo que el resto
        _store.Mock.Verify(s => s.Aviso("Ese mensaje ya no está en el canal"), Times.Once);
    }
}
