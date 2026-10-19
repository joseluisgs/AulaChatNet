using AulaChatNet.Server.Infrastructures;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AulaChatNet.Server.Tests.Integracion;

/// <summary>
/// Prueba de REDES de la señal "está escribiendo...": el ChatHub y el
/// ChatStore de verdad, Kestrel escuchando en un puerto aleatorio y TRES
/// clientes SignalR reales.
///
/// <remarks>
/// La razón de existir de este test es <c>Clients.OthersInGroup</c>: con
/// dobles de Moq se puede fingir cualquier comportamiento, pero lo que
/// importa aquí es que SignalR, por una red de verdad, entregue la señal a
/// todos los de la sala EXCEPTO al que la emite, y que no se cuela en otro
/// canal. Es el mismo enfoque que <c>PrivadosEndToEndTests</c>.
/// </remarks>
/// </summary>
[TestFixture]
public class EscribiendoEndToEndTests
{
    /// <summary>Ruta del hub: la misma en el servidor y en los clientes del test.</summary>
    private const string Ruta = "/hubs/chat";

    /// <summary>Tiempo máximo de espera: si la señal no llega en 5 s, algo va mal.</summary>
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
                [$"{ChatConfig.Seccion}:Canales:1"] = "#smr"
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

    /// <summary>Conecta un cliente REAL y lo da de alta en el canal dado.</summary>
    /// <param name="usuario">Nombre con el que se registra.</param>
    /// <param name="canal">Canal de entrada.</param>
    /// <returns>La conexión, lista para invocar y escuchar.</returns>
    private async Task<HubConnection> ConectarAsync(string usuario, string canal)
    {
        var cliente = new HubConnectionBuilder()
            .WithUrl($"{_servidor.Urls.First()}{Ruta}")
            .Build();

        _clientes.Add(cliente);
        await cliente.StartAsync();

        var error = await cliente.InvokeAsync<string?>(ChatHubMetodos.Conectar, usuario, canal);

        // Si el alta falla, el resto del test no significa nada: mejor parar aquí
        error.Should().BeNull("el alta de {0} debe salir bien", usuario);

        return cliente;
    }

    /// <summary>Suscribe un cliente a las señales de escritura y devuelve la PRIMERA.</summary>
    /// <param name="cliente">Cliente que va a escuchar.</param>
    /// <returns>La primera señal que le llegue (espera como máximo <see cref="Espera"/>).</returns>
    private static Task<string> PrimerEscribiendoQueLlegue(HubConnection cliente)
    {
        var recepcion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        cliente.On<string>(
            ChatHubEventos.Escribiendo, usuario => recepcion.TrySetResult(usuario));

        return recepcion.Task.WaitAsync(Espera);
    }

    [Test]
    public async Task SenalDeEscritura_LlegaAlRestoDelCanalYNoAlQueEscribe()
    {
        // Arrange: Ana y Luis hablando en #general, Marta en otra sala
        var ana = await ConectarAsync("Ana", "#general");
        var luis = await ConectarAsync("Luis", "#general");
        var marta = await ConectarAsync("Marta", "#smr");

        var llegaALuis = PrimerEscribiendoQueLlegue(luis);
        var llegaAAna = PrimerEscribiendoQueLlegue(ana);
        var llegaAMarta = PrimerEscribiendoQueLlegue(marta);

        // Act: la señal viaja por la red de verdad (sin texto: solo el nombre)
        var error = await ana.InvokeAsync<string?>(ChatHubMetodos.Escribiendo);

        // Assert: Luis, de su misma sala, se entera
        error.Should().BeNull();
        var senal = await llegaALuis;
        senal.Should().Be("Ana");

        // ...pero ni la propia Ana (su indicador no es noticia para ella)
        // ni Marta, que está en #smr
        await Task.Delay(300); // margen para un envío que no debe existir
        llegaAAna.IsCompleted.Should().BeFalse("el que escribe no debe recibir su propia señal");
        llegaAMarta.IsCompleted.Should().BeFalse("Marta está en otro canal");
    }
}
