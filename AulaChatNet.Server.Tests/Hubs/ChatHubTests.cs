using AulaChatNet.Server.Hubs;
using AulaChatNet.Server.Models;
using AulaChatNet.Server.Persistencia;
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

    /// <summary>
    /// Eventos emitidos con Clients.All (compartido por todos los hubs del
    /// test): aquí caen los broadcasts como EstadisticasActualizadas.
    /// </summary>
    private List<Envio> _aTodos = null!;

    /// <summary>
    /// Eventos emitidos a una conexión CONCRETA: los de <c>Clients.Client</c>
    /// (destino de los mensajes privados) y los de
    /// <c>Clients.OthersInGroup</c>, que aquí se registran POR MIEMBRO para
    /// poder mirar qué llegó a cada uno — y, sobre todo, qué NO le llegó al
    /// que escribe. Compartido por todos los hubs del test.
    /// </summary>
    private Dictionary<string, List<Envio>> _porConexion = null!;

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
        _aTodos = [];
        _porConexion = new Dictionary<string, List<Envio>>(StringComparer.OrdinalIgnoreCase);
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
        clientes.Setup(c => c.All).Returns(CrearProxy(_aTodos).Object);
        clientes.Setup(c => c.Group(It.IsAny<string>()))
            .Returns<string>(nombre => CrearProxy(ObtenerLista(porCanal, nombre)).Object);

        // Clients.Client(connectionId): el destino de los mensajes privados.
        // Sin esto, un hub que emitiera a una conexión concreta recibiría un
        // proxy nulo de Moq y el test no comprobaría NADA de a quién llegó.
        clientes.Setup(c => c.Client(It.IsAny<string>()))
            .Returns<string>(id => CrearProxy(ObtenerLista(_porConexion, id)).Object);

        // Clients.OthersInGroup(canal): todos los miembros MENOS esta
        // conexión (la señal de escritura no vuelve al que escribe). Se
        // resuelve en el MOMENTO de la llamada, porque los miembros van
        // cambiando según quién se dé de alta en cada test
        clientes.Setup(c => c.OthersInGroup(It.IsAny<string>()))
            .Returns<string>(canal => CrearProxyVarios(ListasDeMiembros(canal, connectionId)).Object);

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

    /// <summary>
    /// Devuelve (creándola si hace falta) la lista de eventos registrados
    /// bajo una clave: un CANAL o una CONEXIÓN, según qué dicte el test.
    /// </summary>
    private static List<Envio> ObtenerLista(Dictionary<string, List<Envio>> porCanal, string nombre)
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

    /// <summary>
    /// Listas de eventos de los miembros del canal, SIN la conexión dada:
    /// así se comporta <c>Clients.OthersInGroup</c>, que no devuelve el aviso
    /// a quien lo emitió.
    /// </summary>
    /// <param name="canal">Canal cuyos miembros se buscan.</param>
    /// <param name="excluida">Conexión que debe quedarse sin eventos.</param>
    private List<List<Envio>> ListasDeMiembros(string canal, string excluida)
    {
        var listas = new List<List<Envio>>();

        if (!_grupos.TryGetValue(canal, out var miembros))
            return listas;

        foreach (var conexion in miembros)
        {
            if (!conexion.Equals(excluida, StringComparison.OrdinalIgnoreCase))
                listas.Add(ObtenerLista(_porConexion, conexion));
        }

        return listas;
    }

    /// <summary>Crea un proxy simulado que registra cada evento en TODAS las listas dadas.</summary>
    private static Mock<ISingleClientProxy> CrearProxyVarios(List<List<Envio>> destinos)
    {
        var proxy = new Mock<ISingleClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((metodo, argumentos, _) =>
            {
                foreach (var lista in destinos)
                    lista.Add(new Envio(metodo, argumentos));
            })
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
        var eventos = ObtenerLista(porCanal, General);

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
        ObtenerLista(porCanal, General).Should().BeEmpty();
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

    [Test]
    public async Task ConnectAsync_Conectado_EmiteEstadisticasATodos()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        await hub.ConnectAsync("Ana");

        // Assert: los contadores (total y por sala) van a TODOS, no solo al canal
        var eventos = _aTodos
            .Where(e => e.Metodo == ChatHubEventos.EstadisticasActualizadas).ToArray();
        eventos.Should().ContainSingle();

        var est = (EstadisticasDto)eventos[0].Argumentos[0];
        est.TotalConectados.Should().Be(1);
        est.Canales.Should().ContainSingle(c => c.Canal == General && c.Conectados == 1);
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
    public async Task CambiarCanal_ActualizaLasEstadisticasGlobales()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        _aTodos.Clear();

        // Act
        (await hub.CambiarCanalAsync(Smr)).Should().BeNull();

        // Assert: #general queda fuera del reparto y #smr pasa a 1
        var eventos = _aTodos
            .Where(e => e.Metodo == ChatHubEventos.EstadisticasActualizadas).ToArray();
        eventos.Should().ContainSingle();

        var est = (EstadisticasDto)eventos[0].Argumentos[0];
        est.TotalConectados.Should().Be(1);
        est.Canales.Should().ContainSingle(c => c.Canal == Smr && c.Conectados == 1);
    }

    [Test]
    public async Task CambiarCanal_Valido_EmiteSalidaEnElViejoYEntradaEnElNuevo()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerLista(porCanal, General).Clear();
        ObtenerLista(porCanal, Smr).Clear();

        // Act
        var error = await hub.CambiarCanalAsync("#smr");

        // Assert
        error.Should().BeNull();

        var salida = ObtenerLista(porCanal, General)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        salida.Should().ContainSingle();
        ((MensajeDto)salida[0].Argumentos[0]).Texto.Should().Be("Ana ha salido del canal");

        var entrada = ObtenerLista(porCanal, Smr)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        entrada.Should().ContainSingle();
        ((MensajeDto)entrada[0].Argumentos[0]).Texto.Should().Be("Ana se ha unido al canal");

        // Ambos canales reciben su lista actualizada
        ObtenerLista(porCanal, General)
            .Should().Contain(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada);
        ObtenerLista(porCanal, Smr)
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
        ObtenerLista(porCanal, General).Clear();

        // Act
        var error = await hub.CambiarCanalAsync("#GENERAL"); // mayúsculas

        // Assert
        error.Should().BeNull();
        ObtenerLista(porCanal, General).Should().BeEmpty();
    }

    #endregion

    #region CambiarNickAsync Tests

    [Test]
    public async Task CambiarNick_NombreValido_EmiteElAvisoYLaListaEnSuCanal()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerLista(porCanal, General).Clear();

        // Act
        var error = await hub.CambiarNickAsync("Ana2");

        // Assert
        error.Should().BeNull();

        var avisos = ObtenerLista(porCanal, General)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        avisos.Should().ContainSingle();
        var mensaje = (MensajeDto)avisos[0].Argumentos[0];
        mensaje.Texto.Should().Be("Ana ahora se llama Ana2");
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);

        // El panel de conectados se actualiza con el nombre nuevo
        var listas = ObtenerLista(porCanal, General)
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
        ObtenerLista(porCanal, General).Clear();

        // Act: mayúsculas distintas, mismo nombre
        var error = await hub.CambiarNickAsync("ANA");

        // Assert
        error.Should().BeNull();
        ObtenerLista(porCanal, General).Should().BeEmpty();
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
        ObtenerLista(porCanal, Smr).Clear();

        // Act
        var error = await hub.SendAsync("Buenas a todos");

        // Assert
        error.Should().BeNull();

        var difundidos = ObtenerLista(porCanal, Smr)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        difundidos.Should().ContainSingle();

        var mensaje = (MensajeDto)difundidos[0].Argumentos[0];
        mensaje.Usuario.Should().Be("Ana");
        mensaje.Texto.Should().Be("Buenas a todos");
        mensaje.Tipo.Should().Be(TipoMensaje.Normal);

        // #general no debe recibir nada
        ObtenerLista(porCanal, General).Should().BeEmpty();
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

    #region EscribiendoAsync Tests

    [Test]
    public async Task EscribiendoAsync_SinRegistro_DevuelveErrorSinEmitirNada()
    {
        // Arrange: el hub es API pública y se invoca a pelo, así que se
        // defiende igual que los demás métodos
        var (hub, _, _) = CrearHub("conn-9");

        // Act
        var error = await hub.EscribiendoAsync();

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
        _porConexion.Should().BeEmpty();
    }

    [Test]
    public async Task EscribiendoAsync_AUnCompanero_LosLlegaAElYNoAlQueEscribe()
    {
        // Arrange: Ana y Luis en el mismo canal
        var (ana, _, _) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis");

        // Act
        var error = await ana.EscribiendoAsync();

        // Assert: a Luis le llega SOLO el nombre de Ana (sin texto: este
        // evento jamás lleva lo que se está escribiendo)
        error.Should().BeNull();
        var avisos = ObtenerLista(_porConexion, "conn-luis")
            .Where(e => e.Metodo == ChatHubEventos.Escribiendo).ToArray();
        avisos.Should().ContainSingle();
        avisos[0].Argumentos[0].Should().Be("Ana");

        // ...y a Ana no: su propio indicador no es noticia para ella
        ObtenerLista(_porConexion, "conn-ana")
            .Where(e => e.Metodo == ChatHubEventos.Escribiendo).Should().BeEmpty();
    }

    [Test]
    public async Task EscribiendoAsync_NoLlegaAQuienEstaEnOtroCanal()
    {
        // Arrange: Marta está en #smr, no en la sala de Ana
        var (ana, _, _) = CrearHub("conn-ana");
        var (marta, _, _) = CrearHub("conn-marta");
        await ana.ConnectAsync("Ana");
        await marta.ConnectAsync("Marta", "#smr");

        // Act
        await ana.EscribiendoAsync();

        // Assert
        ObtenerLista(_porConexion, "conn-marta")
            .Where(e => e.Metodo == ChatHubEventos.Escribiendo).Should().BeEmpty();
    }

    [Test]
    public async Task EscribiendoAsync_NoTocaElHistorialNiLaColaDeEscritura()
    {
        // Arrange: el store CON persistencia, el único camino hasta SQLite
        _store = new ChatStore(Options.Create(_config), new Mock<IMensajesRepository>().Object);

        var (ana, _, _) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis");

        // Se vacía la cola (las uniones SÍ se encolan) para que lo que venga
        // después sea solo culpa de lo que se prueba aquí
        while (_store.ColaEscritura.TryRead(out _))
        {
        }

        var historialAntes = _store.GetHistorial(General).Count;

        // Act
        await ana.EscribiendoAsync();

        // Assert: ni una fila más de historial ni nada en la cola...
        _store.GetHistorial(General).Should().HaveCount(historialAntes);
        _store.ColaEscritura.TryRead(out _).Should().BeFalse();

        // ...y el CONTRASTE: un mensaje normal sí se encola. Sin esto, el
        // test aprobaría también con la cola rota, y eso sería peor
        await ana.SendAsync("Mensaje normal del canal");
        _store.ColaEscritura.TryRead(out var pendiente).Should().BeTrue();
        pendiente.Mensaje.Texto.Should().Be("Mensaje normal del canal");
    }

    #endregion

    #region EnviarPrivadoAsync Tests

    [Test]
    public async Task EnviarPrivadoAsync_ConexionNoRegistrada_DevuelveError()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-5");

        // Act: alguien que no se ha dado de alta intenta escribir en privado
        var error = await hub.EnviarPrivadoAsync("Luis", "Hola");

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
        _porConexion.Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivadoAsync_AUnCompaneroDeCanal_LosLlegaALosDos()
    {
        // Arrange
        var (ana, alAna, _) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis");

        // Act
        var error = await ana.EnviarPrivadoAsync("Luis", "¿nos vemos en el recreo?");

        // Assert: lo recibe el destinatario, por Clients.Client...
        error.Should().BeNull();

        var recibidos = ObtenerLista(_porConexion, "conn-luis")
            .Where(e => e.Metodo == ChatHubEventos.PrivadoRecibido).ToArray();
        recibidos.Should().ContainSingle();

        // ...y el emisor lo recibe por Clients.Caller, con el MISMO objeto
        // (mismo Id): el eco lo pinta el servidor, no lo inventa el cliente
        var ecos = alAna.Where(e => e.Metodo == ChatHubEventos.PrivadoRecibido).ToArray();
        ecos.Should().ContainSingle();
        ecos[0].Argumentos[0].Should().Be(recibidos[0].Argumentos[0]);

        var privado = (MensajePrivadoDto)recibidos[0].Argumentos[0];
        privado.Emisor.Should().Be("Ana");
        privado.Destinatario.Should().Be("Luis");
        privado.Texto.Should().Be("¿nos vemos en el recreo?");
    }

    [Test]
    public async Task EnviarPrivadoAsync_AUnCompanero_NoLlegaNiAlCanalNiATerceros()
    {
        // Arrange: un tercero conectado en la MISMA sala no debe enterarse
        var (ana, _, porCanal) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        var (roberto, alRoberto, _) = CrearHub("conn-roberto");

        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis");
        await roberto.ConnectAsync("Roberto");
        ObtenerLista(porCanal, General).Clear();

        // Act
        await ana.EnviarPrivadoAsync("Luis", "Confidencia");

        // Assert: ni al GRUPO del canal...
        ObtenerLista(porCanal, General)
            .Where(e => e.Metodo == ChatHubEventos.PrivadoRecibido).Should().BeEmpty();

        // ...ni a la conexión del tercero
        alRoberto.Where(e => e.Metodo == ChatHubEventos.PrivadoRecibido).Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivadoAsync_DosHomonimosEnDistintosCanales_SoloAlcanzaAlDelTuyo()
    {
        // Arrange: Luis en #general y OTRO Luis en #smr. Sin mirar el canal,
        // "Luis" no identifica a una persona, y eso es lo que comprueba test
        var (ana, _, _) = CrearHub("conn-ana");
        var (luisGeneral, _, _) = CrearHub("conn-luis-general");
        var (luisSmr, _, _) = CrearHub("conn-luis-smr");

        await ana.ConnectAsync("Ana");
        await luisGeneral.ConnectAsync("Luis");
        await luisSmr.ConnectAsync("Luis", "#smr");

        // Act: Ana está en #general, así que su privado es para el Luis de SU sala
        var error = await ana.EnviarPrivadoAsync("luis", "Para el de mi sala");

        // Assert
        error.Should().BeNull();
        ObtenerLista(_porConexion, "conn-luis-general")
            .Where(e => e.Metodo == ChatHubEventos.PrivadoRecibido).Should().ContainSingle();
        ObtenerLista(_porConexion, "conn-luis-smr").Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivadoAsync_AAlguienDeOtroCanal_DevuelveErrorDeDestino()
    {
        // Arrange: Ana en #general y Luis en #smr: no se pueden hablar
        var (ana, _, _) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis", "#smr");

        // Act
        var error = await ana.EnviarPrivadoAsync("Luis", "Hola");

        // Assert
        error.Should().Be(_config.ErrorPrivadoSinDestino("Luis"));
        _porConexion.Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivadoAsync_AUnNombreInexistente_DevuelveErrorDeDestino()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        var error = await hub.EnviarPrivadoAsync("Maria", "¿Estás ahí?");

        // Assert
        error.Should().Be(_config.ErrorPrivadoSinDestino("Maria"));
        _porConexion.Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivadoAsync_AUnoMismo_DevuelveErrorSinBuscarDestino()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        var error = await hub.EnviarPrivadoAsync("ana", "Hola, soy yo");

        // Assert: el nombre se compara sin distinguir mayúsculas, como todo
        error.Should().Be(_config.ErrorPrivadoAUnoMismo);
        _porConexion.Should().BeEmpty();
    }

    [Test]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task EnviarPrivadoAsync_SinDestinatario_DevuelveError(string? destino)
    {
        // Arrange: el parser no deja pasar esto, pero el hub es PÚBLICO y
        // cualquiera puede invocarlo a pelo, así que se defiende solo
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");

        // Act
        var error = await hub.EnviarPrivadoAsync(destino, "Hola");

        // Assert
        error.Should().Be(_config.ErrorPrivadoDestinoVacio);
        _porConexion.Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivadoAsync_TextoVacio_DevuelveElMismoErrorQueUnMensaje()
    {
        // Arrange
        var (ana, _, _) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis");

        // Act: un privado se valida EXACTAMENTE igual que un mensaje normal
        var error = await ana.EnviarPrivadoAsync("Luis", "   ");

        // Assert
        error.Should().Be(_config.ErrorMensajeVacio);
        _porConexion.Should().BeEmpty();
    }

    [Test]
    public async Task EnviarPrivadoAsync_MensajeCorrecto_NoEntraEnElHistorialDelCanal()
    {
        // Arrange
        var (ana, _, _) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis");

        var antes = _store.GetHistorial(General).Count;

        // Act
        await ana.EnviarPrivadoAsync("Luis", "Esto es una confidencia");

        // Assert: el historial sigue igual que al principio — ni una fila
        // más, y ninguna que contenga el texto. Sin esto, quien entre después
        // en #general se encontraría los privados de los demás.
        _store.GetHistorial(General).Should().HaveCount(antes);
        _store.GetHistorial(General)
            .Should().NotContain(m => m.Texto.Contains("confidencia", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task EnviarPrivadoAsync_MensajeCorrecto_NoEntraEnLaColaDeEscritura()
    {
        // Arrange: el MISMO store que el de producción con persistencia (Fase 2),
        // que es el único camino que lleva mensajes a SQLite
        _store = new ChatStore(Options.Create(_config), new Mock<IMensajesRepository>().Object);

        var (ana, _, _) = CrearHub("conn-ana");
        var (luis, _, _) = CrearHub("conn-luis");
        await ana.ConnectAsync("Ana");
        await luis.ConnectAsync("Luis");

        // Las uniones de los dos SÍ se encolan (son mensajes de sistema del
        // canal): se vacía la cola para que lo que venga después sea solo
        // culpa de lo que se prueba aquí
        while (_store.ColaEscritura.TryRead(out _))
        {
        }

        // Act
        await ana.EnviarPrivadoAsync("Luis", "Esto no debe tocar el disco");

        // Assert: la cola sigue vacía — el privado no ha llegado ni a la
        // puerta de la base de datos...
        _store.ColaEscritura.TryRead(out _).Should().BeFalse();

        // ...y un mensaje normal del canal SÍ se encola. Sin este contraste,
        // el test aprobaría también con la cola rota, y eso sería peor
        await ana.SendAsync("Mensaje normal del canal");
        _store.ColaEscritura.TryRead(out var pendiente).Should().BeTrue();
        pendiente.Mensaje.Texto.Should().Be("Mensaje normal del canal");
    }

    #endregion

    #region OnDisconnectedAsync Tests

    [Test]
    public async Task OnDisconnectedAsync_UsuarioRegistrado_EmiteLaSalidaYSuListaEnSuCanal()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana", "#smr");
        ObtenerLista(porCanal, Smr).Clear();

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert
        var avisos = ObtenerLista(porCanal, Smr)
            .Where(e => e.Metodo == ChatHubEventos.MensajeNuevo).ToArray();
        avisos.Should().ContainSingle();

        var salida = (MensajeDto)avisos[0].Argumentos[0];
        salida.Texto.Should().Be("Ana ha salido del canal");
        salida.Tipo.Should().Be(TipoMensaje.Sistema);

        ObtenerLista(porCanal, Smr)
            .Should().Contain(e => e.Metodo == ChatHubEventos.ListaUsuariosActualizada);
        _store.GetUsuarios(Smr).Should().BeEmpty();

        // Otros canales no se enteran
        ObtenerLista(porCanal, General).Should().BeEmpty();
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
    public async Task OnDisconnectedAsync_ActualizaLasEstadisticasSinElQueSeFue()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana", Smr);
        _aTodos.Clear();

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert: nadie queda en el reparto global
        var eventos = _aTodos
            .Where(e => e.Metodo == ChatHubEventos.EstadisticasActualizadas).ToArray();
        eventos.Should().ContainSingle();

        var est = (EstadisticasDto)eventos[0].Argumentos[0];
        est.TotalConectados.Should().Be(0);
        est.Canales.Should().BeEmpty();
    }

    [Test]
    public async Task OnDisconnectedAsync_ConExcepcion_NoLanzaYEmiteLaSalida()
    {
        // Arrange
        var (hub, _, porCanal) = CrearHub("conn-1");
        await hub.ConnectAsync("Ana");
        ObtenerLista(porCanal, General).Clear();

        // Act: caída por excepción (corte de red...)
        Func<Task> acto = async () => await hub.OnDisconnectedAsync(new Exception("Corte de red"));

        // Assert
        await acto.Should().NotThrowAsync();
        ObtenerLista(porCanal, General)
            .Should().Contain(e => e.Metodo == ChatHubEventos.MensajeNuevo);
    }

    #endregion

    #region ObtenerUsuariosAsync Tests

    [Test]
    public async Task ObtenerUsuarios_DeOtroCanal_DevuelveLaListaSinEstarEnEl()
    {
        // Arrange: Ana en #general, Luis en #smr; NO hay que estar en la sala
        var (primero, _, _) = CrearHub("conn-1");
        await primero.ConnectAsync("Ana");
        var (segundo, _, _) = CrearHub("conn-2");
        await segundo.ConnectAsync("Luis", Smr);

        // Act: Luis consulta #general sin cambiar de canal
        var usuarios = await segundo.ObtenerUsuariosAsync(General);

        // Assert
        usuarios.Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public async Task ObtenerUsuarios_CanalDesconocido_DevuelveNull()
    {
        // Arrange
        var (hub, _, _) = CrearHub("conn-1");

        // Act
        var usuarios = await hub.ObtenerUsuariosAsync("#facil");

        // Assert: null = "el servidor no conoce ese canal"
        usuarios.Should().BeNull();
    }

    [Test]
    public async Task ObtenerUsuarios_SinRegistro_DevuelveLaListaIgual()
    {
        // Arrange: una pestaña puede consultar ANTES de darse de alta
        var (consultador, _, _) = CrearHub("conn-9");

        // Act
        var usuarios = await consultador.ObtenerUsuariosAsync(General);

        // Assert
        usuarios.Should().NotBeNull();
    }

    #endregion
}
