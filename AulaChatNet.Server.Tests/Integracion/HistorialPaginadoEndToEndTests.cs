using AulaChatNet.Server.Infrastructures;
using AulaChatNet.Server.Persistencia;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AulaChatNet.Server.Tests.Integracion;

/// <summary>
/// Prueba de REDES de la paginación del historial (Fase 4.3): el hub, el
/// store y SQLite DE VERDAD, con Kestrel en un puerto aleatorio y clientes
/// SignalR reales.
///
/// <remarks>
/// Es la única prueba que enciende la persistencia en el arnés E2E, y no
/// es un capricho: la caché en memoria solo retiene
/// <c>ChatConfig.HistorialMaximo</c> mensajes, así que lo anterior SOLO
/// existe en la base de datos. Con la ventana bajada a 3 se garantiza que
/// cualquier página profunda pisa disco de verdad.
/// </remarks>
/// </summary>
[TestFixture]
public class HistorialPaginadoEndToEndTests
{
    private const string Ruta = "/historial-paginado";
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    private WebApplication _servidor = null!;
    private readonly List<HubConnection> _clientes = [];
    private string _rutaBase = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Base de datos de un solo uso en el temporal: cada test parte de
        // un historial vacío y nadie pisa los datos de otro
        _rutaBase = Path.Combine(Path.GetTempPath(), $"aulachat-paginado-{Guid.NewGuid():N}.db");

        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ChatConfig.Seccion}:HubRuta"] = Ruta,
                [$"{ChatConfig.Seccion}:Canales:0"] = "#general",
                [$"{ChatConfig.Seccion}:Canales:1"] = "#smr",

                // Ventana de caché ENANA a propósito: con 3 mensajes en
                // memoria, cualquier página anterior tiene que salir de
                // SQLite — que es justo lo que se prueba aquí
                [$"{ChatConfig.Seccion}:HistorialMaximo"] = "3",

                // Sin límite de ráfaga: el test escribe 12 mensajes seguidos
                // a propósito (0 = sin límite, regla de la Fase 4.1)
                [$"{ChatConfig.Seccion}:RafagaMensajes"] = "0",

                [$"{PersistenciaConfig.Seccion}:Ruta"] = _rutaBase
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // puerto 0 = que elija el sistema
        builder.Logging.ClearProviders(); // sin esto, la salida de Kestrel ensucia la de los tests

        // Los MISMOS métodos de configuración que usa Program.cs del
        // servidor, más la persistencia (que los otros E2E no encienden)
        builder.Services.AddChatSignalR();
        builder.Services.AddChatServices(configuracion);
        builder.Services.AddPersistencia(configuracion);

        _servidor = builder.Build();
        _servidor.MapChatHub(configuracion);

        // Program.cs migra al arrancar; aquí se hace a mano antes de abrir
        // el puerto, por el mismo camino (Migrate, no EnsureCreated: este
        // proyecto tiene migraciones de verdad y hay que aplicarlas)
        var fabrica = _servidor.Services.GetRequiredService<IDbContextFactory<AulaChatContexto>>();
        await using var contexto = await fabrica.CreateDbContextAsync();
        await contexto.Database.MigrateAsync();

        await _servidor.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var cliente in _clientes)
            await cliente.DisposeAsync();

        await _servidor.DisposeAsync();

        // El fichero se va con el test. Los pools de SQLite retienen
        // conexiones abiertas y, sin soltarlas, Windows no deja borrar
        SqliteConnection.ClearAllPools();

        // Best effort: si algo se quedara abierto, el limpiador del sistema
        // operativo se ocupa del temporal — un fallo aquí no debe nublar un
        // test que salió bien
        try
        {
            File.Delete(_rutaBase);
            File.Delete(_rutaBase + "-wal");
            File.Delete(_rutaBase + "-shm");
        }
        catch (IOException)
        {
        }
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

    /// <summary>
    /// Espera a que la cola de escritura en segundo plano haya persistido
    /// los mensajes. Es FIFO: cuando la base ya tiene el ÚLTIMO, los
    /// anteriores también. Es el mismo "punto de sincronía" que un TCS en
    /// las otras pruebas, pero cruzando el proceso de un servicio en
    /// segundo plano — sin esto, la paginación correría contra una base
    /// a medio escribir y el test parpadearía.
    /// </summary>
    /// <param name="total">Mensajes que deben estar ya en la base de datos.</param>
    private async Task EsperarPersistidosAsync(int total)
    {
        var repo = _servidor.Services.GetRequiredService<IMensajesRepository>();
        var limite = DateTime.UtcNow + Espera;

        while (DateTime.UtcNow < limite)
        {
            if ((await repo.GetUltimosAsync("#general", 100)).Count >= total)
                return;

            await Task.Delay(25);
        }

        throw new TimeoutException($"La base de datos no llegó a {total} mensajes en {Espera}");
    }

    [Test]
    public async Task LasPaginasVanHaciaAtrasSinRepetirNiPerderNada()
    {
        // Arrange: Ana escribe DOCE mensajes en #general. Cada uno con 20
        // ms de separación: el cursor de la paginación es la MARCA DE
        // TIEMPO, y con gemelos en el mismo milisegundo una página podría
        // traer dos a la vez y las aserciones dudarían
        var ana = await CrearClienteAsync();
        await RegistrarAsync(ana, "Ana", "#general");

        for (var i = 1; i <= 12; i++)
        {
            var error = await ana.InvokeAsync<string?>(ChatHubMetodos.Enviar, $"mensaje {i:D2}");
            error.Should().BeNull("el mensaje {0} debe salir bien", i);
            await Task.Delay(20);
        }

        await EsperarPersistidosAsync(12);

        // ...y Beto entra DESPUÉS: su historial inicial es la caché (3).
        // Se captura con TCS (mismo patrón que el resto de E2E): el aviso
        // viaja por el MISMO canal que la invocación, pero no forma parte
        // de su retorno, así que se espera con su propio plazo
        var beto = await CrearClienteAsync();
        var historialRecibido = new TaskCompletionSource<IReadOnlyList<MensajeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        beto.On<IReadOnlyList<MensajeDto>>(
            ChatHubEventos.HistorialRecibido, h => historialRecibido.TrySetResult(h));

        await RegistrarAsync(beto, "Beto", "#general");
        var historialDeBeto = await historialRecibido.Task.WaitAsync(Espera);

        // Assert inicial: los últimos tres normalitos, del antiguo al nuevo
        historialDeBeto.Should().HaveCount(3, "la caché solo retiene HistorialMaximo");
        historialDeBeto.Select(m => m.Texto).Should().Equal("mensaje 10", "mensaje 11", "mensaje 12");

        // Act 1: la primera página pide desde el más antiguo que ya ve
        var pagina1 = await beto.InvokeAsync<IReadOnlyList<MensajeDto>>(
            ChatHubMetodos.Historial, historialDeBeto[0].Timestamp, 3);

        // Assert 1: el cursor (mensaje 10) VUELVE en la página — el rango
        // es inclusivo para no perder gemelos, y el cliente lo descarta por
        // Id. Lo nuevo para Beto es el 8 y el 9
        pagina1.Select(m => m.Texto).Should().Equal("mensaje 08", "mensaje 09", "mensaje 10");

        // Act 2: la página siguiente sigue el cursor que dejó la anterior
        // (el más antiguo de la que acaba de llegar)
        var pagina2 = await beto.InvokeAsync<IReadOnlyList<MensajeDto>>(
            ChatHubMetodos.Historial, pagina1[0].Timestamp, 3);

        // Assert 2: el cursor (m8) vuelve otra vez, y lo nuevo es m6 y m7
        pagina2.Select(m => m.Texto).Should().Equal("mensaje 06", "mensaje 07", "mensaje 08");

        // Act 3: un cursor ANTERIOR a todo lo que hay
        var ninguna = await beto.InvokeAsync<IReadOnlyList<MensajeDto>>(
            ChatHubMetodos.Historial, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3);

        // Assert 3: página vacía = "no queda más historial", y con eso el
        // cliente apaga su botón de cargar más
        ninguna.Should().BeEmpty();
    }

    [Test]
    public async Task SinRegistroNoHayHistorialQuePaginar()
    {
        // Arrange: el cliente está conectado, pero nunca se dio de alta
        var cliente = await CrearClienteAsync();

        // Act: sin registro no hay canal propio, y sin canal no hay nada
        // que enseñar — null es el "no estás registrado" de este método
        var pagina = await cliente.InvokeAsync<IReadOnlyList<MensajeDto>?>(
            ChatHubMetodos.Historial, DateTime.UtcNow, 10);

        // Assert
        pagina.Should().BeNull();
    }

    [Test]
    public async Task PedirUnaPaginaGiganteSeLimitaALaVentana()
    {
        // Arrange: Ana escribe cuatro mensajes y se espera a que la cola
        // de escritura los deje en la base
        var ana = await CrearClienteAsync();
        await RegistrarAsync(ana, "Ana", "#general");

        for (var i = 1; i <= 4; i++)
        {
            await ana.InvokeAsync<string?>(ChatHubMetodos.Enviar, $"mensaje {i:D2}");
            await Task.Delay(20);
        }

        await EsperarPersistidosAsync(4);

        // Act: pide una página GIGANTE desde un cursor en el futuro (es
        // decir, "dame todo lo que tengas")
        var pagina = await ana.InvokeAsync<IReadOnlyList<MensajeDto>?>(
            ChatHubMetodos.Historial, DateTime.UtcNow.AddMinutes(1), 100);

        // Assert: pidió 100, pero una página nunca supera la ventana del
        // sistema (HistorialMaximo = 3 en esta prueba) — y el último es el
        // del cursor inclusivo
        pagina.Should().HaveCount(3);
        pagina![^1].Texto.Should().Be("mensaje 04");
    }
}
