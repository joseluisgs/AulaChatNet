using AulaChatNet.Server.Hubs;
using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AulaChatNet.Server.Tests.Hubs;

/// <summary>
/// Suite de tests para ChatHub.
/// Simula SignalR con Moq: un HubCallerContext por conexión y proxies que
/// registran cada SendCoreAsync, para comprobar QUIÉN recibe cada evento
/// (el historial SOLO al que entra; los mensajes a todos).
/// </summary>
[TestFixture]
public class ChatHubTests
{
    private ChatConfig _config = null!;
    private ChatStore _store = null!;
    private Mock<ILogger<ChatHub>> _logger = null!;

    [SetUp]
    public void Setup()
    {
        _config = new ChatConfig();
        _store = new ChatStore(Options.Create(_config));
        _logger = new Mock<ILogger<ChatHub>>();
    }

    // ── Dobles de SignalR ──

    /// <summary>Registro de una llamada a SendCoreAsync (evento emitido).</summary>
    private record Envio(string Metodo, object[] Argumentos);

    /// <summary>Hub simulado con sus listas de eventos emitidos.</summary>
    private sealed record HubSimulado(ChatHub Hub, List<Envio> AlCaller, List<Envio> ATodos);

    /// <summary>
    /// Crea un hub con su propia conexión y proxies que graban lo emitido.
    /// Cada test crea los hubs que necesite (uno por conexión simulada).
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión simulada.</param>
    private HubSimulado CrearHub(string connectionId)
    {
        var alCaller = new List<Envio>();
        var aTodos = new List<Envio>();

        var proxyCaller = CrearProxy(alCaller);
        var proxyTodos = CrearProxy(aTodos);

        var clientes = new Mock<IHubCallerClients>();
        clientes.Setup(c => c.Caller).Returns(proxyCaller.Object);
        clientes.Setup(c => c.All).Returns(proxyTodos.Object);

        var hub = new ChatHub(_store, Options.Create(_config), _logger.Object)
        {
            Context = Mock.Of<HubCallerContext>(c => c.ConnectionId == connectionId),
            Clients = clientes.Object
        };

        return new HubSimulado(hub, alCaller, aTodos);
    }

    /// <summary>
    /// Crea un proxy simulado que registra cada evento en la lista dada.
    /// Caller devuelve ISingleClientProxy (subtipo de IClientProxy), por eso
    /// el mock es de ese tipo: sirve igualmente para All.
    /// </summary>
    private static Mock<ISingleClientProxy> CrearProxy(List<Envio> envios)
    {
        var proxy = new Mock<ISingleClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((metodo, argumentos, _) =>
                envios.Add(new Envio(metodo, argumentos)))
            .Returns(Task.CompletedTask);

        return proxy;
    }

    #region ConnectAsync Tests

    [Test]
    public async Task ConnectAsync_NombreValido_DevuelveNullYRegistraElUsuario()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        var error = await hub.ConnectAsync("Ana");

        // Assert
        error.Should().BeNull();
        _store.GetUsuarioDeConexion("conn-1").Should().Be("Ana");
    }

    [Test]
    public async Task ConnectAsync_NombreValido_EnviaElHistorialSoloAlQueEntra()
    {
        // Arrange
        var (hub, alCaller, aTodos) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana");

        // Assert: el historial va SOLO al caller, sin saturar a los demás
        alCaller.Count(e => e.Metodo == ChatHubEventos.HistorialRecibido).Should().Be(1);
        aTodos.Should().NotContain(e => e.Metodo == ChatHubEventos.HistorialRecibido);
    }

    [Test]
    public async Task ConnectAsync_NombreValido_EmiteElAvisoDeUnionATodos()
    {
        // Arrange
        var (hub, _, aTodos) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana");

        // Assert
        var avisos = aTodos.Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        avisos.Should().ContainSingle();

        var mensaje = (MensajeDto)avisos[0].Argumentos[0];
        mensaje.Texto.Should().Be("Ana se ha unido al canal");
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);
    }

    [Test]
    public async Task ConnectAsync_NombreValido_NotificaLaListaDeUsuariosATodos()
    {
        // Arrange
        var (hub, _, aTodos) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana");

        // Assert
        var listas = aTodos.Where(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada).ToArray();
        listas.Should().ContainSingle();

        var usuarios = (IReadOnlyList<UsuarioConectadoDto>)listas[0].Argumentos[0];
        usuarios.Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public async Task ConnectAsync_NombreDeUnCaracter_DevuelveErrorDeLongitud()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        var error = await hub.ConnectAsync("A");

        // Assert
        error.Should().Be(_config.ErrorUsuarioLongitud);
        _store.GetUsuarios().Should().BeEmpty();
    }

    [Test]
    public async Task ConnectAsync_Nulo_DevuelveErrorVacio()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act: un cliente roto podría invocar el método sin argumentos
        var error = await hub.ConnectAsync(null);

        // Assert
        error.Should().Be(_config.ErrorUsuarioVacio);
    }

    [Test]
    public async Task ConnectAsync_NombreEnUso_DevuelveErrorDuplicado()
    {
        // Arrange
        var (primero, _, _) = CrearHub("conn-1");
        (await primero.ConnectAsync("Ana")).Should().BeNull();

        var (segundo, _, _) = CrearHub("conn-2");

        // Act: mismo nombre respetando mayúsculas
        var error = await segundo.ConnectAsync("ANA");

        // Assert
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetUsuarios().Should().ContainSingle();
    }

    [Test]
    public async Task ConnectAsync_MismaConexionDosVeces_DevuelveErrorDeConexion()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        (await hub.ConnectAsync("Ana")).Should().BeNull();

        // Act
        var error = await hub.ConnectAsync("Luis");

        // Assert
        error.Should().Be("Esta conexión ya está registrada en el canal");
    }

    [Test]
    public async Task ConnectAsync_NombreConEspacios_LosRecorta()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        var error = await hub.ConnectAsync("  Ana  ");

        // Assert
        error.Should().BeNull();
        _store.GetUsuarios().Should().ContainSingle(u => u.Usuario == "Ana");
    }

    #endregion

    #region SendAsync Tests

    [Test]
    public async Task SendAsync_ConexionNoRegistrada_DevuelveError()
    {
        // Arrange
        var (hub, _, aTodos) = CrearHub("conn-5");

        // Act: alguien que no se ha dado de alta intenta escribir
        var error = await hub.SendAsync("Buenas");

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
        aTodos.Should().BeEmpty();
    }

    [Test]
    public async Task SendAsync_MensajeValido_DevuelveNullYLoDifundeATodos()
    {
        // Arrange
        var (hub, _, aTodos) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        aTodos.Clear();

        // Act
        var error = await hub.SendAsync("Buenas a todos");

        // Assert
        error.Should().BeNull();

        var difundidos = aTodos.Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        difundidos.Should().ContainSingle();

        var mensaje = (MensajeDto)difundidos[0].Argumentos[0];
        mensaje.Usuario.Should().Be("Ana");
        mensaje.Texto.Should().Be("Buenas a todos");
        mensaje.Tipo.Should().Be(TipoMensaje.Normal);
    }

    [Test]
    public async Task SendAsync_MensajeValido_EntraEnElHistorial()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        await hub.SendAsync("Buenas a todos");

        // Assert: unión + mensaje escrito
        var historial = _store.GetHistorial();
        historial.Should().HaveCount(2);
        historial[^1].Texto.Should().Be("Buenas a todos");
        historial[^1].Tipo.Should().Be(TipoMensaje.Normal);
    }

    [Test]
    public async Task SendAsync_Vacio_DevuelveErrorVacio()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        var error = await hub.SendAsync("   ");

        // Assert
        error.Should().Be(_config.ErrorMensajeVacio);
    }

    [Test]
    public async Task SendAsync_MensajeDeQuinientosUnCaracteres_DevuelveErrorDeLongitud()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        var error = await hub.SendAsync(new string('a', 501));

        // Assert
        error.Should().Be(_config.ErrorMensajeLongitud);
    }

    #endregion

    #region OnDisconnectedAsync Tests

    [Test]
    public async Task OnDisconnectedAsync_UsuarioRegistrado_EmiteLaSalidaYLaLista()
    {
        // Arrange
        var (hub, _, aTodos) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        aTodos.Clear();

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert
        var avisos = aTodos.Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        avisos.Should().ContainSingle();

        var salida = (MensajeDto)avisos[0].Argumentos[0];
        salida.Texto.Should().Be("Ana ha salido del canal");
        salida.Tipo.Should().Be(TipoMensaje.Sistema);

        aTodos.Should().Contain(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada);
        _store.GetUsuarios().Should().BeEmpty();
    }

    [Test]
    public async Task OnDisconnectedAsync_SinRegistro_NoEmiteNada()
    {
        // Arrange: una pestaña que se abre y se cierra sin escribir nada
        var (hub, _, aTodos) = CrearHub("conn-9");

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert
        aTodos.Should().BeEmpty();
    }

    [Test]
    public async Task OnDisconnectedAsync_ConExcepcion_NoLanzaYEmiteLaSalida()
    {
        // Arrange
        var (hub, _, aTodos) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        aTodos.Clear();

        // Act: caída por excepción (corte de red...)
        Func<Task> acto = async () => await hub.OnDisconnectedAsync(new Exception("Corte de red"));

        // Assert
        await acto.Should().NotThrowAsync();
        aTodos.Should().Contain(e => e.Metodo == ChatHubEventos.MensajeNuevo);
    }

    #endregion
}
