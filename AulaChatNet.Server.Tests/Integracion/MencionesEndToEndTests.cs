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
/// Prueba de REDES de las menciones (Fase 4.2): el ChatHub y los servicios
/// de verdad, Kestrel escuchando en un puerto aleatorio y clientes
/// SignalR reales.
///
/// <remarks>
/// Lo que aporta frente a las pruebas con dobles: que el aviso dirigido
/// VIAJA por la red (se serializa igual que lo verá el cliente) y que NO
/// se cuela a nadie más — ni al que escribió ni a quien está en otra sala.
/// </remarks>
/// </summary>
[TestFixture]
public class MencionesEndToEndTests
{
    private const string Ruta = "/menciones";
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

    /// <summary>Registra a un cliente en un canal y falla el test si el alta sale mal.</summary>
    private static async Task RegistrarAsync(HubConnection cliente, string usuario, string canal)
    {
        var error = await cliente.InvokeAsync<string?>(ChatHubMetodos.Conectar, usuario, canal);

        error.Should().BeNull("el alta de {0} debe salir bien", usuario);
    }

    [Test]
    public async Task Mencion_LlegaPorLaRedSoloAQuienNombran()
    {
        // Arrange: Ana y Luis en #general, Marta en OTRA sala
        var ana = await CrearClienteAsync();
        var luis = await CrearClienteAsync();
        var marta = await CrearClienteAsync();

        // Cada cliente registra SUS avisos de mención: Ana los espera con un
        // TCS —el aviso viaja por un canal distinto al de la invocación, así
        // que NO llega sincronizado con el return del SendAsync—; Luis y
        // Marta solo graban, para comprobar que NO les llega nada
        var avisoDeAna = new TaskCompletionSource<MencionDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ana.On<MencionDto>(ChatHubEventos.MencionRecibida, m => avisoDeAna.TrySetResult(m));

        var avisosDeLuis = new List<MencionDto>();
        luis.On<MencionDto>(ChatHubEventos.MencionRecibida, m => avisosDeLuis.Add(m));

        var avisosDeMarta = new List<MencionDto>();
        marta.On<MencionDto>(ChatHubEventos.MencionRecibida, m => avisosDeMarta.Add(m));

        await RegistrarAsync(ana, "Ana", "#general");
        await RegistrarAsync(luis, "Luis", "#general");
        await RegistrarAsync(marta, "Marta", "#smr");

        // Act: Luis menciona a Ana en su canal
        var error = await luis.InvokeAsync<string?>(
            ChatHubMetodos.Enviar, "@Ana ¿me pasas el ejercicio 3?");

        // Assert: el mensaje salió bien — la mención no lo convierte en otra
        // cosa, solo añade un aviso aparte
        error.Should().BeNull();

        // ...y el AVISO viajó por la red hasta Ana, con su emisor y su canal
        var mencion = await avisoDeAna.Task.WaitAsync(Espera);
        mencion.Emisor.Should().Be("Luis");
        mencion.Canal.Should().Be("#general");
        mencion.Texto.Should().Contain("@Ana");

        // Para lo que NO debe llegar hace falta un punto de sincronía en
        // cada conexión: una ida y vuelta más. Si el servidor hubiera
        // enviado el aviso a otra persona, ese envío habría sido ANTERIOR a
        // la respuesta de esta llamada —SignalR ordena por conexión—, así
        // que al contestar ya estaría registrado en la lista
        await luis.InvokeAsync(ChatHubMetodos.ObtenerUsuarios, "#general");
        await marta.InvokeAsync(ChatHubMetodos.ObtenerUsuarios, "#smr");

        avisosDeLuis.Should().BeEmpty("quien escribe ya sabe que escribió");
        avisosDeMarta.Should().BeEmpty("Marta está en #smr, no en la mención");
    }
}
