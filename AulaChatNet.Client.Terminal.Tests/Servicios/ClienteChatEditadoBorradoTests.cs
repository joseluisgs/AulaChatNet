using AulaChatNet.Client.Terminal.Servicios;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;

namespace AulaChatNet.Client.Terminal.Tests.Servicios;

/// <summary>
/// Fase 4.4 en la terminal: los manejadores de MensajeEditado y MensajeBorrado
/// (la vista se reescribe SOLO desde los eventos del servidor, igual que en la
/// web: aquí no manda nadie localmente) y los métodos salientes EditarAsync y
/// BorrarAsync con sus desenlaces: éxito (null), rechazo con texto del
/// servidor y fallo de red.
///
/// <remarks>
/// Los eventos llegan por el transporte real (Kestrel + WebSockets en loopback),
/// así que tras difundir hay que esperar a que el cliente los consuma: para eso
/// está <see cref="EsperarAsync"/>, un sondeo con tope de cinco segundos.
/// </remarks>
/// </summary>
[TestFixture]
public class ClienteChatEditadoBorradoTests
{
    private const string PuertoCaido = "http://127.0.0.1:59999/hubs/chat";

    /// <summary>Sondea hasta que la condición sea cierta, o lanza al pasar el tope.</summary>
    /// <param name="condicion">Lo que esperamos que ocurra en la vista del cliente.</param>
    private static async Task EsperarAsync(Func<bool> condicion)
    {
        var tope = DateTime.UtcNow.AddSeconds(5);

        while (!condicion() && DateTime.UtcNow < tope)
            await Task.Delay(50);

        condicion().Should().BeTrue("el evento del servidor debe llegar antes del tope");
    }

    private static MensajeDto Mensaje(string texto, Guid? id = null, DateTime? hora = null) =>
        new(
            id ?? Guid.NewGuid(),
            "Ana",
            texto,
            hora ?? new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc),
            TipoMensaje.Normal);

    #region MensajeEditado Tests

    [Test]
    public async Task MensajeEditado_SustituyeElTextoEnLaVista_ConservandoIdYHora()
    {
        // Arrange: servidor que acepta y cliente ya dentro del canal
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();

        var original = Mensaje("Buenas a todos");
        await servidor.DifundirMensajeNuevoAsync(original);
        await EsperarAsync(() => cliente.VistaMensajes.Count == 1);

        // Act: llega el eco del servidor con el mensaje COMPLETO reescrito
        var editado = original with { Texto = "Buenas tardes a todos", Editado = true };
        await servidor.DifundirMensajeEditadoAsync(editado);

        // Assert: misma fila (mismo Id y hora), texto nuevo y la marca de editado
        await EsperarAsync(() => cliente.VistaMensajes.Single().Editado);

        var visto = cliente.VistaMensajes.Single();
        visto.Id.Should().Be(original.Id, "el Id no cambia al editar");
        visto.Timestamp.Should().Be(original.Timestamp, "la hora original es inmutable");
        visto.Texto.Should().Be("Buenas tardes a todos");
        visto.Editado.Should().BeTrue();
    }

    [Test]
    public async Task MensajeEditado_IdNoEstaEnLaVista_NoHayQueHacerNada()
    {
        // Arrange: un mensaje en la vista y un editado de OTRO Id (la página
        // que aún no se pidió): no hay nada que sustituir, y no debe reventar
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();

        await servidor.DifundirMensajeNuevoAsync(Mensaje("El único que hay"));
        await EsperarAsync(() => cliente.VistaMensajes.Count == 1);

        // Act: editan un mensaje que este cliente no tiene en la vista
        await servidor.DifundirMensajeEditadoAsync(
            Mensaje("De otra página") with { Editado = true });

        // Assert: el mensaje de la vista queda intacto y no aparece ningún extra
        await Task.Delay(300); // margen para que un eventual error llegara a notarse
        cliente.VistaMensajes.Should().ContainSingle()
            .Which.Texto.Should().Be("El único que hay");
    }

    #endregion MensajeEditado Tests

    #region MensajeBorrado Tests

    [Test]
    public async Task MensajeBorrado_QuitaElMensajeDeLaVista()
    {
        // Arrange
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();

        var mensaje = Mensaje("Esto no va a durar");
        await servidor.DifundirMensajeNuevoAsync(mensaje);
        await EsperarAsync(() => cliente.VistaMensajes.Count == 1);

        // Act: solo llega el Id, como en el contrato real
        await servidor.DifundirMensajeBorradoAsync(mensaje.Id);

        // Assert
        await EsperarAsync(() => cliente.VistaMensajes.Count == 0);
        cliente.VistaMensajes.Should().BeEmpty();
    }

    [Test]
    public async Task MensajeBorrado_QuitaTambienLosChipsDelMensaje()
    {
        // Arrange: un mensaje con su chip de reacción ya asentado en la vista
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();

        var mensaje = Mensaje("Con su chip encima");
        await servidor.DifundirMensajeNuevoAsync(mensaje);
        await servidor.DifundirReaccionActualizadaAsync(
            new ReaccionesMensajeDto(mensaje.Id, [new ReaccionDto("👍", ["Ana"])]));
        await EsperarAsync(() => cliente.VistaReacciones.ContainsKey(mensaje.Id));

        // Act: el borrado debe liberar también los chips (basura sin dueño)
        await servidor.DifundirMensajeBorradoAsync(mensaje.Id);

        // Assert
        await EsperarAsync(() => !cliente.VistaReacciones.ContainsKey(mensaje.Id));
        cliente.VistaMensajes.Should().BeEmpty();
        cliente.VistaReacciones.Should().NotContainKey(mensaje.Id);
    }

    #endregion MensajeBorrado Tests

    #region EditarAsync / BorrarAsync Tests

    [Test]
    public async Task EditarAsync_CuandoElServidorRespondeNull_NoHayError()
    {
        // Arrange: hub que acepta sin preparar ningún rechazo (null = todo bien)
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();

        // Act
        var error = await cliente.EditarAsync(Guid.NewGuid(), "Texto corregido");

        // Assert
        error.Should().BeNull("null es el contrato de éxito del hub");
    }

    [Test]
    public async Task EditarAsync_CuandoElServidorRechaza_DevuelveElError()
    {
        // Arrange: el hub responderá con este texto, como el real al no ser el dueño
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();
        servidor.RespuestaEdicion = "No puedes editar mensajes de otros";

        // Act
        var error = await cliente.EditarAsync(Guid.NewGuid(), "Texto corregido");

        // Assert: el texto del servidor se traslada tal cual para que la
        // terminal lo pinte en la línea de estado
        error.Should().Be("No puedes editar mensajes de otros");
    }

    [Test]
    public async Task BorrarAsync_CuandoElServidorRespondeNull_NoHayError()
    {
        // Arrange
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();

        // Act
        var error = await cliente.BorrarAsync(Guid.NewGuid());

        // Assert
        error.Should().BeNull();
    }

    [Test]
    public async Task BorrarAsync_CuandoElServidorRechaza_DevuelveElError()
    {
        // Arrange
        await using var servidor = await ServidorDePrueba.CrearAceptandoAsync();
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", new ChatConfig { Canales = ["#general"] });
        (await cliente.ConectarAsync("Ana", "#general")).Exitoso.Should().BeTrue();
        servidor.RespuestaBorrado = "No puedes borrar mensajes de otros";

        // Act
        var error = await cliente.BorrarAsync(Guid.NewGuid());

        // Assert
        error.Should().Be("No puedes borrar mensajes de otros");
    }

    [Test]
    public async Task EditarAsync_SinServidor_DevuelveFalloDeRed()
    {
        // Arrange: nadie escuchando en ese puerto
        using var cliente = new ClienteChat(PuertoCaido, new ChatConfig { Canales = ["#general"] });

        // Act
        var error = await cliente.EditarAsync(Guid.NewGuid(), "Texto corregido");

        // Assert: mismo traductor que EnviarAsync, para la línea de estado
        error.Should().Contain("Sin conexión con el servidor");
    }

    [Test]
    public async Task BorrarAsync_SinServidor_DevuelveFalloDeRed()
    {
        // Arrange
        using var cliente = new ClienteChat(PuertoCaido, new ChatConfig { Canales = ["#general"] });

        // Act
        var error = await cliente.BorrarAsync(Guid.NewGuid());

        // Assert
        error.Should().Contain("Sin conexión con el servidor");
    }

    #endregion EditarAsync / BorrarAsync Tests
}
