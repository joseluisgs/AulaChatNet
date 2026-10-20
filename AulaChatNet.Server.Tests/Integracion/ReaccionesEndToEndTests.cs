using AulaChatNet.Server.Infrastructures;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AulaChatNet.Server.Tests.Integracion;

/// <summary>
/// Prueba de REDES de las reacciones: el ChatHub y el ChatStore de verdad,
/// Kestrel escuchando en un puerto aleatorio y clientes SignalR reales.
///
/// <remarks>
/// La razón de existir de este test es lo que los dobles NO pueden
/// garantizar: que el JSON de verdad serialize y deserialice los DTO de
/// reacciones (con sus listas dentro) y que la emisión a
/// <c>Clients.Group</c> entregue a todos los de la sala — el emisor
/// incluido, a diferencia de la señal de escritura. Es el mismo enfoque que
/// <c>EscribiendoEndToEndTests</c> y <c>PrivadosEndToEndTests</c>.
/// </remarks>
/// </summary>
[TestFixture]
public class ReaccionesEndToEndTests
{
    /// <summary>Ruta del hub: la misma en el servidor y en los clientes del test.</summary>
    private const string Ruta = "/hubs/chat";

    /// <summary>Tiempo máximo de espera: si el evento no llega en 5 s, algo va mal.</summary>
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    private WebApplication _servidor = null!;
    private readonly List<HubConnection> _clientes = [];

    [SetUp]
    public async Task SetUp()
    {
        // Misma configuración que el appsettings del servidor real, pero en
        // memoria: así no hay que tocar ficheros ni puertos fijos
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ChatConfig.Seccion}:HubRuta"] = Ruta,
                [$"{ChatConfig.Seccion}:Canales:0"] = "#general",
                [$"{ChatConfig.Seccion}:Canales:1"] = "#smr",

                // Sin cuota de ráfagas: estas pruebas mandan tandas
                // seguidas sin esperar la ventana (0 = sin límite)
                [$"{ChatConfig.Seccion}:RafagaMensajes"] = "0"
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // puerto 0 = que elija el sistema
        builder.Logging.ClearProviders(); // sin esto, la salida de Kestrel ensucia la de los tests

        // Los MISMOS métodos de configuración que usa Program.cs del servidor
        builder.Services.AddChatSignalR();
        builder.Services.AddChatServices(configuracion);

        _servidor = builder.Build();
        _servidor.MapChatHub(configuracion);

        await _servidor.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var cliente in _clientes)
            await cliente.DisposeAsync();

        await _servidor.DisposeAsync();
    }

    /// <summary>Crea un cliente SignalR REAL conectado al servidor de prueba.</summary>
    /// <returns>La conexión, aún sin registrar en ningún canal.</returns>
    private async Task<HubConnection> CrearClienteAsync()
    {
        var cliente = new HubConnectionBuilder()
            .WithUrl($"{_servidor.Urls.First()}{Ruta}")
            .Build();

        _clientes.Add(cliente);
        await cliente.StartAsync();

        return cliente;
    }

    /// <summary>Da de alta un cliente ya conectado en el canal dado.</summary>
    /// <param name="cliente">Cliente conectado.</param>
    /// <param name="usuario">Nombre con el que se registra.</param>
    /// <param name="canal">Canal de entrada.</param>
    private static async Task RegistrarAsync(HubConnection cliente, string usuario, string canal)
    {
        var error = await cliente.InvokeAsync<string?>(ChatHubMetodos.Conectar, usuario, canal);

        // Si el alta falla, el resto del test no significa nada: parar aquí
        error.Should().BeNull("el alta de {0} debe salir bien", usuario);
    }

    /// <summary>
    /// Entra a un canal como lo hace el cliente de verdad: se suscribe al
    /// historial PRIMERO y se registra DESPUÉS, porque el historial no es el
    /// resultado de la invocación sino un EVENTO a Clients.Caller.
    /// </summary>
    /// <param name="usuario">Nombre con el que se registra.</param>
    /// <param name="canal">Canal de entrada.</param>
    /// <returns>La conexión y el historial que le llegó al entrar.</returns>
    private async Task<(HubConnection Cliente, IReadOnlyList<MensajeDto> Historial)> EntrarAsync(string usuario, string canal)
    {
        var cliente = await CrearClienteAsync();

        var recepcion = new TaskCompletionSource<IReadOnlyList<MensajeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        cliente.On<IReadOnlyList<MensajeDto>>(
            ChatHubEventos.HistorialRecibido, historial => recepcion.TrySetResult(historial));

        await RegistrarAsync(cliente, usuario, canal);

        var historial = await recepcion.Task.WaitAsync(Espera);

        return (cliente, historial);
    }

    /// <summary>Suscribe un cliente a los mensajes NORMALES del canal y devuelve el próximo.</summary>
    /// <param name="cliente">Cliente que va a escuchar.</param>
    /// <returns>El siguiente mensaje de persona que le llegue (espera como máximo <see cref="Espera"/>).</returns>
    private static Task<MensajeDto> ProximoMensajeNormalQueLlegue(HubConnection cliente)
    {
        var recepcion = new TaskCompletionSource<MensajeDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        cliente.On<MensajeDto>(ChatHubEventos.MensajeNuevo, mensaje =>
        {
            if (mensaje.Tipo == TipoMensaje.Normal)
                recepcion.TrySetResult(mensaje);
        });

        return recepcion.Task.WaitAsync(Espera);
    }

    /// <summary>Suscribe un cliente al estado de UN mensaje y devuelve el primer evento.</summary>
    /// <param name="cliente">Cliente que va a escuchar.</param>
    /// <returns>La primera reacción actualizada que le llegue (espera como máximo <see cref="Espera"/>).</returns>
    private static Task<ReaccionesMensajeDto> PrimerEstadoQueLlegue(HubConnection cliente)
    {
        var recepcion = new TaskCompletionSource<ReaccionesMensajeDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        cliente.On<ReaccionesMensajeDto>(
            ChatHubEventos.ReaccionActualizada, estado => recepcion.TrySetResult(estado));

        return recepcion.Task.WaitAsync(Espera);
    }

    [Test]
    public async Task Reaccion_LlegaAlCanalCompletoConElEmisorIncluidoYNoA_OtraSala()
    {
        // Arrange: Ana y Luis en #general, Marta en otra sala
        var (ana, _) = await EntrarAsync("Ana", "#general");
        var luis = await ConectarLimpioAsync("Luis", "#general");
        var marta = await ConectarLimpioAsync("Marta", "#smr");

        var proximo = ProximoMensajeNormalQueLlegue(ana);
        await ana.InvokeAsync<string?>(ChatHubMetodos.Enviar, "¿Alguien ha entendido el ejercicio 3?");
        var mensaje = await proximo;

        var llegaAAna = PrimerEstadoQueLlegue(ana);
        var llegaALuis = PrimerEstadoQueLlegue(luis);
        var llegaAMarta = PrimerEstadoQueLlegue(marta);

        // Act: la reacción viaja por la red de verdad
        var error = await ana.InvokeAsync<string?>(ChatHubMetodos.Reaccionar, mensaje.Id, "👍");

        // Assert: el emisor también se entera (a diferencia de la señal de
        // escritura, aquí su chip es noticia para todos, él incluido)
        error.Should().BeNull();
        var mio = await llegaAAna;
        mio.IdMensaje.Should().Be(mensaje.Id);
        mio.Reacciones.Should().ContainSingle();

        // ...y su compañero de sala, con el mismo contenido y su nombre
        var deLuis = await llegaALuis;
        deLuis.IdMensaje.Should().Be(mensaje.Id);
        deLuis.Reacciones[0].Personas.Should().Equal("Ana");

        // ...pero nada cruza a la sala de Marta
        await Task.Delay(300); // margen para un envío que no debe existir
        llegaAMarta.IsCompleted.Should().BeFalse("Marta está en otro canal");
    }

    [Test]
    public async Task Reaccion_LaSegundaVezQuitaElChip()
    {
        // Arrange
        var (ana, _) = await EntrarAsync("Ana", "#general");

        var proximo = ProximoMensajeNormalQueLlegue(ana);
        await ana.InvokeAsync<string?>(ChatHubMetodos.Enviar, "Buenas a todos");
        var mensaje = await proximo;

        var primera = PrimerEstadoQueLlegue(ana);
        await ana.InvokeAsync<string?>(ChatHubMetodos.Reaccionar, mensaje.Id, "👍");
        (await primera).Reacciones.Should().ContainSingle();

        // Act: el mismo gesto otra vez lo quita
        var segunda = PrimerEstadoQueLlegue(ana);
        var error = await ana.InvokeAsync<string?>(ChatHubMetodos.Reaccionar, mensaje.Id, "👍");

        // Assert: la lista llega vacía, que es como el cliente sabe que
        // debe borrar la fila entera
        error.Should().BeNull();
        var estado = await segunda;
        estado.IdMensaje.Should().Be(mensaje.Id);
        estado.Reacciones.Should().BeEmpty();
    }

    [Test]
    public async Task QuienEntraDespues_RecibeElSnapshotDeReacciones()
    {
        // Arrange: Ana deja un mensaje con chip ANTES de que exista Luis
        var (ana, _) = await EntrarAsync("Ana", "#general");

        var proximo = ProximoMensajeNormalQueLlegue(ana);
        await ana.InvokeAsync<string?>(ChatHubMetodos.Enviar, "¿Nos vemos el jueves?");
        var mensaje = await proximo;
        await ana.InvokeAsync<string?>(ChatHubMetodos.Reaccionar, mensaje.Id, "😂");

        // Act: Luis se suscribe PRIMERO y se registra DESPUÉS — el snapshot
        // llega durante el registro, como evento a Clients.Caller
        var luis = await CrearClienteAsync();

        var recepcion = new TaskCompletionSource<IReadOnlyList<ReaccionesMensajeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        luis.On<IReadOnlyList<ReaccionesMensajeDto>>(
            ChatHubEventos.ReaccionesRecibidas, reacciones => recepcion.TrySetResult(reacciones));

        await RegistrarAsync(luis, "Luis", "#general");

        // Assert: quien llega tarde ve el chip del mensaje antiguo
        var snapshot = await recepcion.Task.WaitAsync(Espera);
        snapshot.Should().ContainSingle();
        snapshot[0].IdMensaje.Should().Be(mensaje.Id);
        snapshot[0].Reacciones.Should().ContainSingle()
            .Which.Personas.Should().Equal("Ana");
    }

    /// <summary>Conecta y registra a un cliente sin necesitar su historial.</summary>
    /// <param name="usuario">Nombre con el que se registra.</param>
    /// <param name="canal">Canal de entrada.</param>
    /// <returns>La conexión, lista para invocar y escuchar.</returns>
    private async Task<HubConnection> ConectarLimpioAsync(string usuario, string canal)
    {
        var cliente = await CrearClienteAsync();
        await RegistrarAsync(cliente, usuario, canal);

        return cliente;
    }
}
