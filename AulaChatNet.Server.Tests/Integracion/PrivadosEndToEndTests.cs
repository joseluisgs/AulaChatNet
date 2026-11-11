using AulaChatNet.Server.Infrastructures;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AulaChatNet.Server.Tests.Integracion;

/// <summary>
///     Prueba de REDES del mensaje privado: el <c>ChatHub</c> y el
///     <c>ChatStore</c> de verdad, Kestrel escuchando en un puerto aleatorio y
///     TRES clientes SignalR reales.
///     <remarks>
///         Los tests con dobles de Moq ya dicen a QUIÉN debe llegar cada cosa; este
///         comprueba que el mensaje VIAJA de verdad — el DTO serializado, el nombre
///         del evento y el transporte— cosa que un doble no puede demostrar porque ni
///         siquiera usa la red. Es el mismo truco que ya usaba
///         <c>ServidorDePrueba</c> de la terminal, pero con el hub auténtico.
///     </remarks>
/// </summary>
[TestFixture]
public class PrivadosEndToEndTests {
    [SetUp]
    public async Task SetUp() {
        // Misma configuración que el appsettings del servidor real, pero en
        // memoria: así no hay que tocar ficheros ni puertos fijos
        IConfigurationRoot configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                [$"{ChatConfig.Seccion}:HubRuta"] = Ruta,
                [$"{ChatConfig.Seccion}:Canales:0"] = "#general",
                [$"{ChatConfig.Seccion}:Canales:1"] = "#smr",

                // Sin cuota de ráfagas: estas pruebas mandan tandas
                // seguidas sin esperar la ventana (0 = sin límite)
                [$"{ChatConfig.Seccion}:RafagaMensajes"] = "0"
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
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
    public async Task TearDown() {
        foreach (HubConnection cliente in _clientes) {
            await cliente.DisposeAsync();
        }

        await _servidor.DisposeAsync();
    }

    /// <summary>Ruta del hub: la misma en el servidor y en los clientes del test.</summary>
    private const string Ruta = "/hubs/chat";

    /// <summary>Tiempo máximo de espera: si un privado no llega en 5 s, algo va mal.</summary>
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    private WebApplication _servidor = null!;
    private readonly List<HubConnection> _clientes = [];

    /// <summary>Conecta un cliente REAL y lo da de alta en el canal dado.</summary>
    /// <param name="usuario">Nombre con el que se registra.</param>
    /// <param name="canal">Canal de entrada.</param>
    /// <returns>La conexión, lista para invocar y escuchar.</returns>
    private async Task<HubConnection> ConectarAsync(string usuario, string canal) {
        HubConnection cliente = new HubConnectionBuilder()
            .WithUrl($"{_servidor.Urls.First()}{Ruta}")
            .Build();

        _clientes.Add(cliente);
        await cliente.StartAsync();

        string? error = await cliente.InvokeAsync<string?>(ChatHubMetodos.Conectar, usuario, canal);

        // Si el alta falla, el resto del test no significa nada: mejor parar aquí
        error.Should().BeNull("el alta de {0} debe salir bien", usuario);

        return cliente;
    }

    /// <summary>Suscribe un cliente a los privados y devuelve la PRIMERA recepción.</summary>
    /// <param name="cliente">Cliente que va a escuchar.</param>
    /// <returns>El primer privado que le llegue (espera como máximo <see cref="Espera" />).</returns>
    private static Task<MensajePrivadoDto> PrimerPrivadoQueLlegue(HubConnection cliente) {
        var recepcion = new TaskCompletionSource<MensajePrivadoDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        cliente.On<MensajePrivadoDto>(
            ChatHubEventos.PrivadoRecibido, privado => recepcion.TrySetResult(privado));

        return recepcion.Task.WaitAsync(Espera);
    }

    [Test]
    public async Task PrivadoEntreDosClientesReales_LosLlegaSoloALosDos() {
        // Arrange: tres personas en #general, y una cuarta en otra sala
        HubConnection ana = await ConectarAsync("Ana", "#general");
        HubConnection luis = await ConectarAsync("Luis", "#general");
        HubConnection roberto = await ConectarAsync("Roberto", "#general");
        HubConnection deOtraSala = await ConectarAsync("Marta", "#smr");

        Task<MensajePrivadoDto> llegaALuis = PrimerPrivadoQueLlegue(luis);
        Task<MensajePrivadoDto> llegaAAna = PrimerPrivadoQueLlegue(ana);

        // Act: el privado viaja por la red de verdad
        string? error = await ana.InvokeAsync<string?>(
            ChatHubMetodos.EnviarPrivado, "Luis", "Confidencia real");

        // Assert: los dos implicados lo reciben, con el mismo texto
        error.Should().BeNull();

        MensajePrivadoDto paraLuis = await llegaALuis;
        MensajePrivadoDto ecoDeAna = await llegaAAna;

        paraLuis.Emisor.Should().Be("Ana");
        paraLuis.Destinatario.Should().Be("Luis");
        paraLuis.Texto.Should().Be("Confidencia real");
        ecoDeAna.Id.Should().Be(paraLuis.Id);

        // Y a nadie más: ni al compañero de su sala ni al de otra
        Task<MensajePrivadoDto> llegaARoberto = PrimerPrivadoQueLlegue(roberto);
        Task<MensajePrivadoDto> llegaAMarta = PrimerPrivadoQueLlegue(deOtraSala);

        await Task.Delay(300); // margen para un envío que no debe existir

        llegaARoberto.IsCompleted.Should().BeFalse("Roberto no debe enterarse");
        llegaAMarta.IsCompleted.Should().BeFalse("Marta está en otra sala");
    }

    [Test]
    public async Task Privado_AUnNombreInexistente_LlegaElErrorYNoSeEnviaNada() {
        // Arrange
        HubConnection ana = await ConectarAsync("Ana", "#general");
        HubConnection luis = await ConectarAsync("Luis", "#general");
        Task<MensajePrivadoDto> llegaALuis = PrimerPrivadoQueLlegue(luis);

        // Act
        string? error = await ana.InvokeAsync<string?>(
            ChatHubMetodos.EnviarPrivado, "Maria", "¿Estás ahí?");

        // Assert: el error se devuelve por el MISMO camino de la llamada
        error.Should().Contain("Maria");
        await Task.Delay(300);
        llegaALuis.IsCompleted.Should().BeFalse("nadie debe recibir nada si falla");
    }

    [Test]
    public async Task Privado_NoApareceEnElHistorialQueRecibeQuienEntraDespues() {
        // Arrange
        HubConnection ana = await ConectarAsync("Ana", "#general");
        HubConnection luis = await ConectarAsync("Luis", "#general");
        await ana.InvokeAsync<string?>(ChatHubMetodos.EnviarPrivado, "Luis", "Esto es una confidencia");

        // Act: alguien entra DESPUÉS y recibe el historial del canal
        HubConnection roberto = new HubConnectionBuilder()
            .WithUrl($"{_servidor.Urls.First()}{Ruta}")
            .Build();
        _clientes.Add(roberto);

        var historialRecibido = new TaskCompletionSource<IReadOnlyList<MensajeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        roberto.On<IReadOnlyList<MensajeDto>>(
            ChatHubEventos.HistorialRecibido, historial => historialRecibido.TrySetResult(historial));

        await roberto.StartAsync();
        await roberto.InvokeAsync<string?>(ChatHubMetodos.Conectar, "Roberto", "#general");

        IReadOnlyList<MensajeDto> historial = await historialRecibido.Task.WaitAsync(Espera);

        // Assert: el historial trae las uniones, pero NUNCA la confidencia
        historial.Should().NotContain(m =>
            m.Texto.Contains("confidencia", StringComparison.OrdinalIgnoreCase));
    }
}
