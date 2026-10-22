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
/// Prueba de REDES de editar y borrar mensajes propios (Fase 4.4): el hub,
/// el store y SQLite DE VERDAD, con Kestrel en un puerto aleatorio y clientes
/// SignalR reales.
///
/// <remarks>
/// La persistencia va ENCENDIDA a propósito (mismo arnés que la paginación):
/// editar y borran ESCRITURAS DIRECTAS en SQLite —fuera de la cola de
/// insertados—, y eso solo se puede comprobar contra una base de verdad.
/// </remarks>
/// </summary>
[TestFixture]
public class EditarBorrarEndToEndTests
{
    private const string Ruta = "/editar-borrar";
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    private WebApplication _servidor = null!;
    private readonly List<HubConnection> _clientes = [];
    private string _rutaBase = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Base de datos de un solo uso en el temporal: cada test parte de
        // un historial vacío y nadie pisa los datos de otro
        _rutaBase = Path.Combine(Path.GetTempPath(), $"aulachat-editar-{Guid.NewGuid():N}.db");

        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ChatConfig.Seccion}:HubRuta"] = Ruta,
                [$"{ChatConfig.Seccion}:Canales:0"] = "#general",
                [$"{ChatConfig.Seccion}:Canales:1"] = "#smr",

                // Sin límite de ráfaga: los tests mandan mensajes seguidos
                // sin esperar la ventana (0 = sin límite, regla de la 4.1)
                [$"{ChatConfig.Seccion}:RafagaMensajes"] = "0",

                [$"{PersistenciaConfig.Seccion}:Ruta"] = _rutaBase
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // puerto 0 = que elija el sistema
        builder.Logging.ClearProviders(); // sin esto, la salida de Kestrel ensucia la de los tests

        // Los MISMOS métodos de configuración que usa Program.cs del servidor
        builder.Services.AddChatSignalR();
        builder.Services.AddChatServices(configuracion);
        builder.Services.AddPersistencia(configuracion);

        _servidor = builder.Build();
        _servidor.MapChatHub(configuracion);

        // Program.cs migra al arrancar; aquí se hace a mano antes de abrir
        // el puerto (Migrate, no EnsureCreated: hay migraciones de verdad)
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

        // Los pools de SQLite retienen conexiones abiertas y, sin
        // soltarlas, Windows no deja borrar
        SqliteConnection.ClearAllPools();

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
    /// Envía un mensaje y devuelve su DTO, capturado con el MISMO evento que
    /// reciben los demás clientes (el emisor también está en el grupo). Es
    /// el "punto de sincronía" del que cuelgan el resto de eventos.
    /// </summary>
    /// <param name="emisor">Cliente que envía (y que recibe su eco por el grupo).</param>
    /// <param name="texto">Texto del mensaje.</param>
    /// <returns>El mensaje ya difundido al canal.</returns>
    private static async Task<MensajeDto> EnviarYCapturarAsync(HubConnection emisor, string texto)
    {
        var recibido = new TaskCompletionSource<MensajeDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = emisor.On<MensajeDto>(ChatHubEventos.MensajeNuevo,
            m => { if (m.Texto == texto) recibido.TrySetResult(m); });

        var error = await emisor.InvokeAsync<string?>(ChatHubMetodos.Enviar, texto);
        error.Should().BeNull("el mensaje debe salir bien");

        return await recibido.Task.WaitAsync(Espera);
    }

    [Test]
    public async Task LaEdicionLlegaATodoElCanalYQuedaEnSQLite()
    {
        // Arrange: Ana y Beto en #general; Carol, en #smr, no debe enterarse
        var ana = await CrearClienteAsync();
        var beto = await CrearClienteAsync();
        var carol = await CrearClienteAsync();
        await RegistrarAsync(ana, "Ana", "#general");
        await RegistrarAsync(beto, "Beto", "#general");
        await RegistrarAsync(carol, "Carol", "#smr");

        var mensaje = await EnviarYCapturarAsync(ana, "Buenas a todos");

        // El evento llega por el MISMO canal que la invocación, pero no
        // forma parte de su retorno: se captura con TCS (patrón de la casa)
        var editadoParaBeto = new TaskCompletionSource<MensajeDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var leLlegoACarol = false;

        beto.On<MensajeDto>(
            ChatHubEventos.MensajeEditado, m => editadoParaBeto.TrySetResult(m));
        carol.On<MensajeDto>(ChatHubEventos.MensajeEditado, _ => leLlegoACarol = true);

        // Act
        var error = await ana.InvokeAsync<string?>(
            ChatHubMetodos.Editar, mensaje.Id, "Buenas tardes a todos");
        var editado = await editadoParaBeto.Task.WaitAsync(Espera);

        // Assert 1: el mismo Id (los clientes sustituyen su fila en su
        // sitio), el texto nuevo y la marca de editado
        error.Should().BeNull();
        editado.Id.Should().Be(mensaje.Id);
        editado.Texto.Should().Be("Buenas tardes a todos");
        editado.Editado.Should().BeTrue();
        editado.Timestamp.Should().Be(mensaje.Timestamp, "la hora no se mueve al editar");

        // Assert 2: a la sala de al lado NO le llegó nada (el eco de Beto
        // ya es la garantía de que el envío al grupo se completó)
        leLlegoACarol.Should().BeFalse("la edición es del canal del emisor");

        // Assert 3: la escritura fue DIRECTA en SQLite — al volver la
        // invocación, la fila ya está actualizada en disco
        var repo = _servidor.Services.GetRequiredService<IMensajesRepository>();
        var enDisco = (await repo.GetUltimosAsync("#general", 10)).Single(m => m.Id == mensaje.Id);
        enDisco.Texto.Should().Be("Buenas tardes a todos");
        enDisco.Editado.Should().BeTrue();
    }

    [Test]
    public async Task ElBorradoQuitaElMensajeDeLaVistaYDeLaBase()
    {
        // Arrange
        var ana = await CrearClienteAsync();
        var beto = await CrearClienteAsync();
        await RegistrarAsync(ana, "Ana", "#general");
        await RegistrarAsync(beto, "Beto", "#general");

        var mensaje = await EnviarYCapturarAsync(ana, "Este mensaje no va a durar");

        // El INSERT viaja en la cola de escritura en segundo plano, y el
        // borrado es DIRECTO: sin esperar a que persista, se podría borrar
        // ANTES de que la fila existiera y quedaría un zombi en disco
        var repo = _servidor.Services.GetRequiredService<IMensajesRepository>();
        var limite = DateTime.UtcNow + Espera;

        while ((await repo.GetUltimosAsync("#general", 10)).Count == 0
               && DateTime.UtcNow < limite)
        {
            await Task.Delay(25);
        }

        var borradoParaBeto = new TaskCompletionSource<Guid>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        beto.On<Guid>(ChatHubEventos.MensajeBorrado, id => borradoParaBeto.TrySetResult(id));

        // Act
        var error = await ana.InvokeAsync<string?>(ChatHubMetodos.Borrar, mensaje.Id);
        var idBorrado = await borradoParaBeto.Task.WaitAsync(Espera);

        // Assert 1: el eco lleva SOLO el Id — con eso basta para que cada
        // cliente quite la fila si la tiene
        error.Should().BeNull();
        idBorrado.Should().Be(mensaje.Id);

        // Assert 2: la fila ya no está en SQLite; la invocación no vuelve
        // hasta que el DELETE se ejecuta de verdad
        (await repo.GetUltimosAsync("#general", 10)).Should().BeEmpty();
    }

    [Test]
    public async Task EditarElMensajeDeOtro_DevuelveErrorEnEspanol()
    {
        // Arrange: el mensaje es de Ana y Beto intenta editarlo
        var ana = await CrearClienteAsync();
        var beto = await CrearClienteAsync();
        await RegistrarAsync(ana, "Ana", "#general");
        await RegistrarAsync(beto, "Beto", "#general");

        var mensaje = await EnviarYCapturarAsync(ana, "Buenas a todos");

        // El INSERT viaja en la cola de escritura en segundo plano: sin
        // esperar a que persista, la comprobación de "sigue intacto"
        // correría contra una base a medio escribir
        var repo = _servidor.Services.GetRequiredService<IMensajesRepository>();
        var limite = DateTime.UtcNow + Espera;

        while ((await repo.GetUltimosAsync("#general", 10)).Count == 0
               && DateTime.UtcNow < limite)
        {
            await Task.Delay(25);
        }

        // Act
        var error = await beto.InvokeAsync<string?>(
            ChatHubMetodos.Editar, mensaje.Id, "Texto ajeno");

        // Assert: el error vuelve en español y el texto NO cambia
        error.Should().Be("Solo puedes modificar tus propios mensajes");
        (await repo.GetUltimosAsync("#general", 10)).Single().Texto.Should().Be("Buenas a todos");
    }

    [Test]
    public async Task EditarNoVuelveAAvisarDeMencion()
    {
        // Arrange: Ana menciona a Beto — el aviso de mención es un EMPUJONCITO
        // separado del mensaje, y una edición no es un mensaje nuevo
        var ana = await CrearClienteAsync();
        var beto = await CrearClienteAsync();
        await RegistrarAsync(ana, "Ana", "#general");
        await RegistrarAsync(beto, "Beto", "#general");

        var mencionesDeBeto = 0;
        beto.On<MencionDto>(ChatHubEventos.MencionRecibida, _ => Interlocked.Increment(ref mencionesDeBeto));

        var mensaje = await EnviarYCapturarAsync(ana, "Hola @Beto, ¿me escuchas?");

        // Act: Ana edita el mensaje (el texto SIGUE mencionando a Beto). El
        // eco de la edición sirve de sincronía antes de mirar el contador
        var editadoParaAna = new TaskCompletionSource<MensajeDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ana.On<MensajeDto>(ChatHubEventos.MensajeEditado, m => editadoParaAna.TrySetResult(m));

        var error = await ana.InvokeAsync<string?>(
            ChatHubMetodos.Editar, mensaje.Id, "Hola @Beto, te repito la pregunta");
        await editadoParaAna.Task.WaitAsync(Espera);

        // Assert: UNA sola mención (la del envío original): la edición
        // cambia el texto de la fila, pero no vuelve a pitar a nadie
        error.Should().BeNull();
        mencionesDeBeto.Should().Be(1, "editar no es enviar de nuevo");
    }
}
