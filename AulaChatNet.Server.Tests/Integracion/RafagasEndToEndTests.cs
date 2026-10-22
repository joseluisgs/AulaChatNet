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
/// Prueba de REDES del control de ráfagas (Fase 4.1): el ChatHub y los
/// servicios de verdad, Kestrel escuchando en un puerto aleatorio y un
/// cliente SignalR real mandando tandas.
///
/// <remarks>
/// Lo que aporta frente a las pruebas unitarias: que el aviso VIAJA por la
/// red (se serializa y deserializa igual que lo verá el cliente) y que el
/// intento rechazado NO se difunde al canal de nadie.
/// </remarks>
/// </summary>
[TestFixture]
public class RafagasEndToEndTests
{
    private const string Ruta = "/rafaga";

    private WebApplication _servidor = null!;
    private readonly List<HubConnection> _clientes = [];

    [SetUp]
    public async Task SetUp()
    {
        // Misma configuración que el appsettings del servidor real, pero en
        // memoria y con una cuota corta: 3 mensajes cada 10 segundos, para
        // que el test dure lo justo y la ventana no caduque por el camino
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ChatConfig.Seccion}:HubRuta"] = Ruta,
                [$"{ChatConfig.Seccion}:Canales:0"] = "#general",
                [$"{ChatConfig.Seccion}:Canales:1"] = "#smr",
                [$"{ChatConfig.Seccion}:RafagaMensajes"] = "3",
                [$"{ChatConfig.Seccion}:RafagaVentanaSegundos"] = "10"
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

    [Test]
    public async Task Enviar_RapidoDeMasElAvisoLlegaPorLaRedYNadaSeDifunde()
    {
        // Arrange: Ana entra en #general y escucha los mensajes del canal
        var ana = await CrearClienteAsync();

        var mensajes = new List<MensajeDto>();
        ana.On<MensajeDto>(ChatHubEventos.MensajeNuevo, m => mensajes.Add(m));

        var errorAlEntrar = await ana.InvokeAsync<string?>(
            ChatHubMetodos.Conectar, "Ana", "#general");
        errorAlEntrar.Should().BeNull("el alta debe salir bien");

        // Act: cuatro mensajes SIN esperar (la cuota es de 3)
        var resultados = new List<string?>();
        for (var i = 1; i <= 4; i++)
            resultados.Add(await ana.InvokeAsync<string?>(ChatHubMetodos.Enviar, $"mensaje {i}"));

        // Assert: los tres primeros pasan y el cuarto ve el aviso por la red
        resultados[..3].Should().OnlyContain(r => r == null);
        resultados[3].Should().Contain("Demasiados mensajes seguidos");

        // Y el canal se queda con los tres que SÍ cabían: el cuarto se cayó
        mensajes.Where(m => m.Tipo == TipoMensaje.Normal)
            .Should().HaveCount(3, "el intento rechazado no debe difundirse");
    }

    [Test]
    public async Task Enviar_CuotaPorConexion_OtrosUsuariosNoLaNotan()
    {
        // Arrange: dos clientes en el MISMO canal
        var ana = await CrearClienteAsync();
        var luis = await CrearClienteAsync();

        (await ana.InvokeAsync<string?>(ChatHubMetodos.Conectar, "Ana", "#general"))
            .Should().BeNull();
        (await luis.InvokeAsync<string?>(ChatHubMetodos.Conectar, "Luis", "#general"))
            .Should().BeNull();

        // Act: Ana agota SU cuota...
        var deAna = new List<string?>();
        for (var i = 1; i <= 4; i++)
            deAna.Add(await ana.InvokeAsync<string?>(ChatHubMetodos.Enviar, $"ana {i}"));

        // ... y Luis escribe a su ritmo
        var deLuis = await luis.InvokeAsync<string?>(ChatHubMetodos.Enviar, "yo escribo lo mío");

        // Assert: la cuota es de la CONEXIÓN, no del canal ni del aula
        deAna[^1].Should().Contain("Demasiados mensajes seguidos");
        deLuis.Should().BeNull("la cuota de Ana no puede pagarla Luis");
    }
}
