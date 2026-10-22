using AulaChatNet.Client.Blazor.Components.Shared;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Moq;

namespace AulaChatNet.Client.Blazor.Components.Tests.Components;

/// <summary>
/// Pruebas de una fila de mensaje: nombre en su color, hora LOCAL (el
/// servidor guarda UTC) y el formato de sistema (gris cursiva, sin avatar).
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class MensajeItemTests : BunitContext
{
    [SetUp]
    public void SetUp()
    {
        // ChatConfig para el avatar y la store falsa porque la fila pinta
        // ahora el componente de reacciones, que habla con la store
        Services.AddSingleton(new ChatConfig());
        Services.AddSingleton(StoreFalsa.Crear().Objeto);

        // El editor inline pone el foco al abrirse (JS interop). En bUnit
        // el JSInterop es ESTRICTO: toda llamada hay que declararla, o el
        // test falla por un interop "inesperado"
        JSInterop.SetupVoid("aulachat.enfocar", _ => true).SetVoidResult();
    }

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

    #region Editar y borrar Tests

    [Test]
    public void MensajePropio_AsaleLosBotonesDeEditarYBorrar()
    {
        // Arrange: el mensaje es de Ana y ella es quien lo mira
        var mensaje = Mensaje("Ana", "Hola a todos");

        // Act
        var cut = Render<MensajeItem>(p =>
        {
            p.Add(m => m.Mensaje, mensaje);
            p.Add(m => m.Nick, "Ana");
        });

        // Assert: ✏️ y 🗑️, en ese mismo orden
        var botones = cut.FindAll(".boton-edicion");
        botones.Should().HaveCount(2);
        botones[0].TextContent.Should().Be("✏️");
        botones[1].TextContent.Should().Be("🗑️");
    }

    [Test]
    public void MensajeAjeno_NoTieneBotonesDeEdicion()
    {
        // Arrange: el mensaje es de Luis y quien mira es Ana
        var mensaje = Mensaje("Luis", "Hola a todos");

        // Act
        var cut = Render<MensajeItem>(p =>
        {
            p.Add(m => m.Mensaje, mensaje);
            p.Add(m => m.Nick, "Ana");
        });

        // Assert: el servidor también lo rechazaría, pero no hace falta ni
        // preguntar — el botón ni siquiera se pinta
        cut.FindAll(".boton-edicion").Should().BeEmpty();
    }

    [Test]
    public void MensajeDeSistema_NoTieneBotonesDeEdicion()
    {
        // Arrange: los avisos de sistema no los edita nadie
        var mensaje = new MensajeDto(
            Guid.NewGuid(), MensajesSistema.Autor, "Luis se ha unido al canal.",
            DateTime.UtcNow, TipoMensaje.Sistema);

        // Act
        var cut = Render<MensajeItem>(p =>
        {
            p.Add(m => m.Mensaje, mensaje);
            p.Add(m => m.Nick, "Ana");
        });

        // Assert
        cut.FindAll(".boton-edicion").Should().BeEmpty();
    }

    [Test]
    public void MensajeEditado_LaMarcaVaGrisTrasElTexto()
    {
        // Arrange: el DTO es inmutable; editar es una COPIA con la marca
        var mensaje = Mensaje("Ana", "Hola a todos") with { Editado = true };

        // Act
        var cut = Render<MensajeItem>(p => p.Add(m => m.Mensaje, mensaje));

        // Assert: el MISMO "(editado)" que pinta la terminal (Shared)
        cut.Find(".mensaje-editado").TextContent.Should().Be("(editado)");
    }

    [Test]
    public void MensajeSinEditar_NoTieneMarca()
    {
        // Arrange
        var mensaje = Mensaje("Ana", "Hola a todos");

        // Act
        var cut = Render<MensajeItem>(p => p.Add(m => m.Mensaje, mensaje));

        // Assert
        cut.FindAll(".mensaje-editado").Should().BeEmpty();
    }

    [Test]
    public void Editar_AbreElEditorInlineConElTextoActual()
    {
        // Arrange
        var mensaje = Mensaje("Ana", "Hola a todos");

        // Act: clic en ✏️ (el primer botón)
        var cut = Render<MensajeItem>(p =>
        {
            p.Add(m => m.Mensaje, mensaje);
            p.Add(m => m.Nick, "Ana");
        });

        cut.Find(".boton-edicion").Click();

        // Assert: el texto se PRELLENA, para corregir en vez de reescribir
        var editor = cut.Find(".editor-inline");
        editor.GetAttribute("value").Should().Be("Hola a todos");
        cut.FindAll(".mensaje-texto").Should().BeEmpty("el editor sustituye al texto");
    }

    [Test]
    public void Editar_EnterGuardaElTextoNuevo()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        var mensaje = Mensaje("Ana", "Hola a todos");
        var cut = RenderizarConStore(store, mensaje);

        cut.Find(".boton-edicion").Click();

        // Act: se cambia el texto (cada tecla dispara oninput) y Enter guarda
        cut.Find(".editor-inline").Input("Hola a todos, ¿qué tal?");
        cut.Find(".editor-inline").KeyDown(Key.Enter);

        // Assert: la store solo INVOCAL al hub — el eco por evento es el
        // que repinta la fila
        store.Mock.Verify(
            s => s.EditarMensajeAsync(mensaje.Id, "Hola a todos, ¿qué tal?"), Times.Once);
    }

    [Test]
    public void Editar_EscapeCancelaSinTocarAlServidor()
    {
        // Arrange
        var store = StoreFalsa.Crear();
        var mensaje = Mensaje("Ana", "Hola a todos");
        var cut = RenderizarConStore(store, mensaje);

        cut.Find(".boton-edicion").Click();
        cut.Find(".editor-inline").Input("Texto que se arrepiente");

        // Act
        cut.Find(".editor-inline").KeyDown(Key.Escape);

        // Assert: vuelve el texto original y ni una invocación
        cut.Find(".mensaje-texto").TextContent.Should().Be("Hola a todos");
        store.Mock.Verify(
            s => s.EditarMensajeAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void Borrar_PideLaConfirmacionEnLaFilaSinTocarAlServidor()
    {
        // Arrange: 🗑️ no borra al vuelo, primero la fila pregunta
        var store = StoreFalsa.Crear();
        var mensaje = Mensaje("Ana", "Hola a todos");
        var cut = RenderizarConStore(store, mensaje);

        // Act: 🗑️ es el SEGUNDO botón
        cut.FindAll(".boton-edicion")[1].Click();

        // Assert: la ✏️ y 🗑️ se apartan y asoman ✔ y ✕ — nada de diálogos
        // del navegador, la confirmación vive en la propia fila
        var botones = cut.FindAll(".boton-edicion");
        botones.Should().HaveCount(2);
        botones[0].GetAttribute("title").Should().Be("Confirmar el borrado");
        botones[1].GetAttribute("title").Should().Be("Cancelar el borrado");
        store.Mock.Verify(s => s.BorrarMensajeAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    public void Borrar_ConLaConfirmacionAceptada_LlamaAlStore()
    {
        // Arrange: la fila pregunta y este test acepta con ✔
        var store = StoreFalsa.Crear();
        var mensaje = Mensaje("Ana", "Hola a todos");
        var cut = RenderizarConStore(store, mensaje);
        cut.FindAll(".boton-edicion")[1].Click();

        // Act
        cut.Find("button[title=\"Confirmar el borrado\"]").Click();

        // Assert
        store.Mock.Verify(s => s.BorrarMensajeAsync(mensaje.Id), Times.Once);
    }

    [Test]
    public void Borrar_ConLaConfirmacionCancelada_NoTocaAlServidor()
    {
        // Arrange: el ✕ debe dejar la fila como estaba — el borrado es
        // definitivo, y un "no" a medias no debe pasar de largo
        var store = StoreFalsa.Crear();
        var mensaje = Mensaje("Ana", "Hola a todos");
        var cut = RenderizarConStore(store, mensaje);
        cut.FindAll(".boton-edicion")[1].Click();

        // Act
        cut.Find("button[title=\"Cancelar el borrado\"]").Click();

        // Assert: vuelven ✏️ y 🗑️, sin una sola llamada al store
        var botones = cut.FindAll(".boton-edicion");
        botones.Should().HaveCount(2);
        botones[0].GetAttribute("title").Should().Be("Editar este mensaje");
        botones[1].GetAttribute("title").Should().Be("Borrar este mensaje");
        store.Mock.Verify(s => s.BorrarMensajeAsync(It.IsAny<Guid>()), Times.Never);
    }

    #endregion

    /// <summary>Crea un mensaje de prueba del canal.</summary>
    private static MensajeDto Mensaje(string usuario, string texto) =>
        new(Guid.NewGuid(), usuario, texto, DateTime.UtcNow, TipoMensaje.Normal);

    /// <summary>
    /// Renderiza la fila con SU store falsa, para poder verificar las
    /// llamadas de los botones ✏️/🗑️ y del editor inline.
    /// </summary>
    /// <param name="store">Store a registrar (sustuye a la del SetUp).</param>
    /// <param name="mensaje">Mensaje a pintar.</param>
    /// <returns>El componente renderizado, viendo como "Ana".</returns>
    private IRenderedComponent<MensajeItem> RenderizarConStore(
        StoreFalsa store, MensajeDto mensaje)
    {
        Services.AddSingleton(store.Objeto);

        return Render<MensajeItem>(p =>
        {
            p.Add(m => m.Mensaje, mensaje);
            p.Add(m => m.Nick, "Ana");
        });
    }
}
