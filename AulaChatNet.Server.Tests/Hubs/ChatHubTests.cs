using AulaChatNet.Server.Hubs;
using AulaChatNet.Server.Models;
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
/// Simula SignalR con Moq: un HubCallerContext por conexión, un proxy para
/// el caller y proxies POR GRUPO (canal), para comprobar QUIÉN recibe cada
/// evento y EN QUÉ CANAL (el historial SOLO al que entra; los mensajes al
/// grupo de su canal).
/// </summary>
[TestFixture]
public class ChatHubTests
{
    private const string General = "#general";
    private const string Smr = "#smr";

    private ChatConfig _config = null!;
    private ChatStore _store = null!;
    private Mock<ILogger<ChatHub>> _logger = null!;

    /// <summary>
    /// Miembros de cada grupo simulado (grupo → conexiones), como hace
    /// SignalR: los envíos a Clients.Group solo llega a quien se dio de alta
    /// con Groups.AddToGroupAsync. Compartido por todos los hubs de un test.
    /// </summary>
    private Dictionary<string, HashSet<string>> _grupos = null!;

    [SetUp]
    public void Setup()
    {
        _config = new ChatConfig();

        // Poblamos la lista como haría el ConfigurationBinder con el
        // appsettings de Fase 1 (el binder AÑADE, por eso el default está vacío)
        _config.Canales.AddRange(["#general", "#smr", "#daw", "#asir", "#dam"]);

        _store = new ChatStore(Options.Create(_config));
        _logger = new Mock<ILogger<ChatHub>>();
        _grupos = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    }

    // ── Dobles de SignalR ──

    /// <summary>Registro de una llamada a SendCoreAsync (evento emitido).</summary>
    private record Envio(string Metodo, object[] Argumentos);

    /// <summary>Hub simulado con sus listas de eventos emitidos.</summary>
    private sealed record HubSimulado(ChatHub Hub, List<Envio> AlCaller, Dictionary<string, List<Envio>> PorCanal);

    /// <summary>
    /// Crea un hub con su propia conexión y proxies que graban lo emitido:
    /// el caller registra en su lista y cada grupo (canal) en la suya.
    /// Cada test crea los hubs que necesite (uno por conexión simulada).
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión simulada.</param>
    private HubSimulado CrearHub(string connectionId)
    {
        var alCaller = new List<Envio>();
        var porCanal = new Dictionary<string, List<Envio>>(StringComparer.OrdinalIgnoreCase);

        var proxyCaller = CrearProxy(alCaller);

        var clientes = new Mock<IHubCallerClients>();
        clientes.Setup(c => c.Caller).Returns(proxyCaller.Object);
        clientes.Setup(c => c.Group(It.IsAny<string>()))
            .Returns<string>(nombre => CrearProxy(ObtenerCanal(porCanal, nombre)).Object);

        var hub = new ChatHub(_store, Options.Create(_config), _logger.Object)
        {
            Context = Mock.Of<HubCallerContext>(c => c.ConnectionId == connectionId),
            Clients = clientes.Object,
            Groups = CrearGrupos()
        };

        return new HubSimulado(hub, alCaller, porCanal);
    }

    /// <summary>
    /// Crea el gestor de grupos simulado: mantiene la PERTENENCIA real
    /// (grupo → conexiones) para poder comprobar que el hub da de alta y da
    /// de baja a cada conexión en su canal. Sin esto, un hub que envíe a
    /// Clients.Group sin haberse dado de alta pasaría todos los tests.
    /// </summary>
    private IGroupManager CrearGrupos()
    {
        var grupos = new Mock<IGroupManager>();

        grupos.Setup(g => g.AddToGroupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((conexion, grupo, _) =>
            {
                if (!_grupos.TryGetValue(grupo, out var miembros))
                    _grupos[grupo] = miembros = [];

                miembros.Add(conexion);
            })
            .Returns(Task.CompletedTask);

        grupos.Setup(g => g.RemoveFromGroupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((conexion, grupo, _) =>
            {
                if (_grupos.TryGetValue(grupo, out var miembros))
                    miembros.Remove(conexion);
            })
            .Returns(Task.CompletedTask);

        return grupos.Object;
    }

    /// <summary>¿Está la conexión dada de alta en el grupo (canal) indicado?</summary>
    private bool EstaEnGrupo(string connectionId, string grupo) =>
        _grupos.TryGetValue(grupo, out var miembros) && miembros.Contains(connectionId);

    /// <summary>Devuelve (creándola si hace falta) la lista de eventos de un canal.</summary>
    private static List<Envio> ObtenerCanal(Dictionary<string, List<Envio>> porCanal, string nombre)
    {
        if (!porCanal.TryGetValue(nombre, out var lista))
            porCanal[nombre] = lista = [];

        return lista;
    }

    /// <summary>Crea un proxy simulado que registra cada evento en la lista dada.</summary>
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
    public async Task ConnectAsync_SinCanal_EntraEnElCanalPorDefecto()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        var error = await hub.ConnectAsync("Ana");

        // Assert
        error.Should().BeNull();
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
    }

    [Test]
    public async Task ConnectAsync_CanalEspecifico_EntraEnEseCanal()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        var error = await hub.ConnectAsync("Ana", "smr"); // sin almohadilla

        // Assert
        error.Should().BeNull();
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(Smr, "Ana"));
    }

    [Test]
    public async Task ConnectAsync_CanalDesconocido_DevuelveError()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        var error = await hub.ConnectAsync("Ana", "#facil");

        // Assert
        error.Should().Contain("#facil").And.Contain("#smr");
        _store.GetUsuarios(General).Should().BeEmpty();
    }

    [Test]
    public async Task ConnectAsync_NombreValido_EnviaElHistorialSoloAlQueEntra()
    {
        // Arrange
        var (hub, alCaller, _) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana");

        // Assert: el historial va SOLO al caller, sin saturar a los demás
        alCaller.Count(e => e.Metodo == ChatHubEventos.HistorialRecibido).Should().Be(1);
    }

    [Test]
    public async Task ConnectAsync_NombreValido_EmiteUnionYListaASuCanal()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana");

        // Assert
        var eventos = ObtenerCanal(porCanal, General);

        var avisos = eventos.Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        avisos.Should().ContainSingle();
        var mensaje = (MensajeDto)avisos[0].Argumentos[0];
        mensaje.Texto.Should().Be("Ana se ha unido al canal");
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);

        var listas = eventos.Where(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada).ToArray();
        listas.Should().ContainSingle();

        var usuarios = (IReadOnlyList<UsuarioConectadoDto>)listas[0].Argumentos[0];
        usuarios.Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public async Task ConnectAsync_NombreValido_NoEmiteNadaEnLosDemasCanales()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana", "#smr");

        // Assert: #general no debe recibir nada
        ObtenerCanal(porCanal, General).Should().BeEmpty();
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
        _store.GetUsuarios(General).Should().BeEmpty();
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
    public async Task ConnectAsync_NombreEnUsoEnEseCanal_DevuelveErrorDuplicado()
    {
        // Arrange
        var (primero, _, _) = CrearHub("conn-1");
        (await primero.ConnectAsync("Ana")).Should().BeNull();

        var (segundo, _, _) = CrearHub("conn-2");

        // Act: mismo nombre respetando mayúsculas
        var error = await segundo.ConnectAsync("ANA");

        // Assert
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetUsuarios(General).Should().ContainSingle();
    }

    [Test]
    public async Task ConnectAsync_MismoNombreEnOtroCanal_SePermite()
    {
        // Arrange: una "Ana" en #general
        var (primero, _, _) = CrearHub("conn-1");
        (await primero.ConnectAsync("Ana")).Should().BeNull();

        var (segundo, _, _) = CrearHub("conn-2");

        // Act: otra "Ana" entra en #smr
        var error = await segundo.ConnectAsync("Ana", "#smr");

        // Assert
        error.Should().BeNull();
        _store.GetUsuarios(General).Should().ContainSingle();
        _store.GetUsuarios(Smr).Should().ContainSingle();
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
    public async Task ConnectAsync_DaDeAltaLaConexionEnElGrupoDelCanal()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana");

        // Assert: sin el alta en el grupo, Clients.Group(canal) iría a un
        // grupo vacío y nadie recibiría los mensajes (fallo silencioso)
        EstaEnGrupo("conn-1", General).Should().BeTrue();
    }

    [Test]
    public async Task ConnectAsync_CanalDesconocido_NoDaDeAltaEnNingunGrupo()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana", "#facil");

        // Assert
        _grupos.Should().BeEmpty();
    }

    #endregion

    #region CambiarCanalAsync Tests

    [Test]
    public async Task CambiarCanal_Valido_SaleDelGrupoViejo_YEntraEnElNuevo()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        EstaEnGrupo("conn-1", General).Should().BeTrue();

        // Act
        var error = await hub.CambiarCanalAsync(Smr);

        // Assert: si no saliera del grupo viejo seguiría viendo #general
        error.Should().BeNull();
        EstaEnGrupo("conn-1", General).Should().BeFalse();
        EstaEnGrupo("conn-1", Smr).Should().BeTrue();
    }

    [Test]
    public async Task CambiarCanal_AlMismoCanal_NoTocaLosGrupos()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act: mismo canal → null sin emitir nada (el cliente solo repinta)
        var error = await hub.CambiarCanalAsync(General);

        // Assert
        error.Should().BeNull();
        EstaEnGrupo("conn-1", General).Should().BeTrue();
    }

    [Test]
    public async Task CambiarCanal_Valido_EmiteSalidaEnElViejoYEntradaEnElNuevo()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerCanal(porCanal, General).Clear();
        ObtenerCanal(porCanal, Smr).Clear();

        // Act
        var error = await hub.CambiarCanalAsync("#smr");

        // Assert
        error.Should().BeNull();

        var salida = ObtenerCanal(porCanal, General)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        salida.Should().ContainSingle();
        ((MensajeDto)salida[0].Argumentos[0]).Texto.Should().Be("Ana ha salido del canal");

        var entrada = ObtenerCanal(porCanal, Smr)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        entrada.Should().ContainSingle();
        ((MensajeDto)entrada[0].Argumentos[0]).Texto.Should().Be("Ana se ha unido al canal");

        // Ambos canales reciben su lista actualizada
        ObtenerCanal(porCanal, General)
            .Should().Contain(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada);
        ObtenerCanal(porCanal, Smr)
            .Should().Contain(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada);
    }

    [Test]
    public async Task CambiarCanal_EnviaElHistorialDelNuevoCanalSoloAlQueCambia()
    {
        // Arrange
        var (hub, alCaller, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        alCaller.Clear();

        // Act
        await hub.CambiarCanalAsync(Smr);

        // Assert
        var historiales = alCaller.Where(e => e.Metodo == ChatHubEventos.HistorialRecibido).ToArray();
        historiales.Should().ContainSingle();
    }

    [Test]
    public async Task CambiarCanal_NombreOcupadoEnElDestino_DevuelveErrorYNoSeMueve()
    {
        // Arrange: otra "Ana" vive en #smr
        var (primero, _, _) = CrearHub("conn-1");
        (await primero.ConnectAsync("Ana")).Should().BeNull();

        var (segundo, _, _) = CrearHub("conn-2");
        (await segundo.ConnectAsync("Ana", "#smr")).Should().BeNull();

        // Act: la Ana de #general intenta ir a #smr
        var error = await primero.CambiarCanalAsync("#smr");

        // Assert
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
    }

    [Test]
    public async Task CambiarCanal_CanalDesconocido_DevuelveError()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        var error = await hub.CambiarCanalAsync("#inexistente");

        // Assert
        error.Should().Contain("#inexistente");
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
    }

    [Test]
    public async Task CambiarCanal_SinRegistro_DevuelveError()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-9");

        // Act
        var error = await hub.CambiarCanalAsync("#smr");

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
    }

    [Test]
    public async Task CambiarCanal_AlCanalActual_DevuelveNullSinEmitirNada()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerCanal(porCanal, General).Clear();

        // Act
        var error = await hub.CambiarCanalAsync("#GENERAL"); // mayúsculas

        // Assert
        error.Should().BeNull();
        ObtenerCanal(porCanal, General).Should().BeEmpty();
    }

    #endregion

    #region CambiarNickAsync Tests

    [Test]
    public async Task CambiarNick_NombreValido_EmiteElAvisoYLaListaEnSuCanal()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerCanal(porCanal, General).Clear();

        // Act
        var error = await hub.CambiarNickAsync("Ana2");

        // Assert
        error.Should().BeNull();

        var avisos = ObtenerCanal(porCanal, General)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        avisos.Should().ContainSingle();
        var mensaje = (MensajeDto)avisos[0].Argumentos[0];
        mensaje.Texto.Should().Be("Ana ahora se llama Ana2");
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);

        // El panel de conectados se actualiza con el nombre nuevo
        var listas = ObtenerCanal(porCanal, General)
            .Where(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada).ToArray();
        listas.Should().ContainSingle();

        var usuarios = (IReadOnlyList<UsuarioConectadoDto>)listas[0].Argumentos[0];
        usuarios.Should().ContainSingle(u => u.Usuario == "Ana2");
    }

    [Test]
    public async Task CambiarNick_EntraEnElHistorialDeSuCanal()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        await hub.CambiarNickAsync("Ana2");

        // Assert: unión + cambio de nick en #general
        var historial = _store.GetHistorial(General);
        historial.Should().HaveCount(2);
        historial[^1].Texto.Should().Be("Ana ahora se llama Ana2");
        historial[^1].Tipo.Should().Be(TipoMensaje.Sistema);
    }

    [Test]
    public async Task CambiarNick_NombreOcupado_DevuelveErrorYConservaElViejo()
    {
        // Arrange: Luis ya está en #general
        var (primero, _, _) = CrearHub("conn-1");
        await primero.ConnectAsync("Ana");

        var (segundo, _, _) = CrearHub("conn-2");
        await segundo.ConnectAsync("Luis");

        // Act: Ana intenta llamarse Luis
        var error = await primero.CambiarNickAsync("Luis");

        // Assert
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
    }

    [Test]
    public async Task CambiarNick_NombreInvalido_DevuelveErrorDeLongitud()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        var error = await hub.CambiarNickAsync("A");

        // Assert
        error.Should().Be(_config.ErrorUsuarioLongitud);
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
    }

    [Test]
    public async Task CambiarNick_SinRegistro_DevuelveError()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-9");

        // Act
        var error = await hub.CambiarNickAsync("Ana2");

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
    }

    [Test]
    public async Task CambiarNick_AlMismoNombre_DevuelveNullSinEmitirNada()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerCanal(porCanal, General).Clear();

        // Act: mayúsculas distintas, mismo nombre
        var error = await hub.CambiarNickAsync("ANA");

        // Assert
        error.Should().BeNull();
        ObtenerCanal(porCanal, General).Should().BeEmpty();
    }

    #endregion

    #region SendAsync Tests

    [Test]
    public async Task SendAsync_ConexionNoRegistrada_DevuelveError()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-5");

        // Act: alguien que no se ha dado de alta intenta escribir
        var error = await hub.SendAsync("Buenas");

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
        porCanal.Should().BeEmpty();
    }

    [Test]
    public async Task SendAsync_MensajeValido_DifundeASuCanalYNoAlosDemas()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana", "#smr");
        ObtenerCanal(porCanal, Smr).Clear();

        // Act
        var error = await hub.SendAsync("Buenas a todos");

        // Assert
        error.Should().BeNull();

        var difundidos = ObtenerCanal(porCanal, Smr)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        difundidos.Should().ContainSingle();

        var mensaje = (MensajeDto)difundidos[0].Argumentos[0];
        mensaje.Usuario.Should().Be("Ana");
        mensaje.Texto.Should().Be("Buenas a todos");
        mensaje.Tipo.Should().Be(TipoMensaje.Normal);

        // #general no debe recibir nada
        ObtenerCanal(porCanal, General).Should().BeEmpty();
    }

    [Test]
    public async Task SendAsync_MensajeValido_EntraEnElHistorialDeSuCanal()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        await hub.SendAsync("Buenas a todos");

        // Assert: unión + mensaje escrito en #general; #smr sigue vacío
        var historial = _store.GetHistorial(General);
        historial.Should().HaveCount(2);
        historial[^1].Texto.Should().Be("Buenas a todos");
        historial[^1].Tipo.Should().Be(TipoMensaje.Normal);
        _store.GetHistorial(Smr).Should().BeEmpty();
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
    public async Task OnDisconnectedAsync_UsuarioRegistrado_EmiteLaSalidaYSuListaEnSuCanal()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana", "#smr");
        ObtenerCanal(porCanal, Smr).Clear();

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert
        var avisos = ObtenerCanal(porCanal, Smr)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        avisos.Should().ContainSingle();

        var salida = (MensajeDto)avisos[0].Argumentos[0];
        salida.Texto.Should().Be("Ana ha salido del canal");
        salida.Tipo.Should().Be(TipoMensaje.Sistema);

        ObtenerCanal(porCanal, Smr)
            .Should().Contain(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada);
        _store.GetUsuarios(Smr).Should().BeEmpty();

        // Otros canales no se enteran
        ObtenerCanal(porCanal, General).Should().BeEmpty();
    }

    [Test]
    public async Task OnDisconnectedAsync_SinRegistro_NoEmiteNada()
    {
        // Arrange: una pestaña que se abre y se cierra sin escribir nada
        var (hub, _, porCanal) = CrearHub("conn-9");

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert
        porCanal.Should().BeEmpty();
    }

    [Test]
    public async Task OnDisconnectedAsync_ConExcepcion_NoLanzaYEmiteLaSalida()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerCanal(porCanal, General).Clear();

        // Act: caída por excepción (corte de red...)
        Func<Task> acto = async () => await hub.OnDisconnectedAsync(new Exception("Corte de red"));

        // Assert
        await acto.Should().NotThrowAsync();
        ObtenerCanal(porCanal, General)
            .Should().Contain(e => e.Metodo == ChatHubEventos.MensajeNuevo);
    }

    #endregion
}
