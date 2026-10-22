using AulaChatNet.Client.Web.Services;
using AulaChatNet.Client.Web.State;
using AulaChatNet.Shared.Comandos;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using FluentAssertions;
using Moq;

namespace AulaChatNet.Client.Web.Tests.Services;

/// <summary>
/// Pruebas del procesador de comandos de la web: MISMO parser y MISMAS
/// reglas que la terminal (ChatConfig compartida), ejecutados contra stores
/// simuladas — sin red y sin navegador. Por cada comando se comprueba qué
/// toca en la store y qué se le cuenta al usuario (avisos en el flujo).
/// </summary>
[TestFixture]
public class ProcesadorComandosTests
{
    private StoreFalsa _store = null!;
    private Mock<ISesionStore> _sesion = null!;
    private ChatConfig _config = null!;
    private ProcesadorComandos _procesador = null!;

    [SetUp]
    public void SetUp()
    {
        _store = StoreFalsa.Crear(["#general", "#daw"]);
        _sesion = new Mock<ISesionStore>();
        _config = new ChatConfig
        {
            Canales = ["#general", "#daw"]
        };
        _procesador = new ProcesadorComandos(_store.Objeto, _config, _sesion.Object);
    }

    #region Ayuda Tests

    [Test]
    public async Task AyudaSinTema_EnseniaElCatalogoCompleto()
    {
        // Act
        await _procesador.EjecutarAsync(new Comando.Ayuda(null));

        // Assert: cabecera + comandos del catálogo compartido con la terminal
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("Comandos disponibles") &&
            t.Contains("/canal <nombre>") &&
            t.Contains("/salir"))), Times.Once);
    }

    [Test]
    public async Task AyudaConTema_EnseniaSoloEseComando()
    {
        // Act: "canal" (sin barra) debe bastar
        await _procesador.EjecutarAsync(new Comando.Ayuda("canal"));

        // Assert: sale /canal (y su primo /canales, mismo prefijo) pero no /nick
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("/canal <nombre>") &&
            !t.Contains("/nick <nombre>"))), Times.Once);
    }

    [Test]
    public async Task AyudaConTemaInexistente_AvisaQueNoHayAyuda()
    {
        // Act
        await _procesador.EjecutarAsync(new Comando.Ayuda("magia"));

        // Assert: ni fallo ni lista vacía: se explica y se recuerda /ayuda
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("No hay ayuda") && t.Contains("/ayuda"))), Times.Once);
    }

    #endregion

    #region Canales Tests

    [Test]
    public async Task Canales_MuestraTotalesConteosYElActual()
    {
        // Arrange: estamos en #daw con 2 de las 3 personas conectadas
        _store.CanalActual.OnNext("#daw");
        _store.Estadisticas.OnNext(new EstadisticasDto(3,
            [new ConteoCanalDto("#general", 1), new ConteoCanalDto("#daw", 2)]));

        // Act
        await _procesador.EjecutarAsync(new Comando.Canales());

        // Assert: total, cuenta por sala y marca del canal actual
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("3 conectados") &&
            t.Contains("#general 1") &&
            t.Contains("#daw 2 ● actual"))), Times.Once);
    }

    #endregion

    #region Canal Tests

    [Test]
    public async Task CanalNoValido_AvisaSinTocarLaRed()
    {
        // Act
        await _procesador.EjecutarAsync(new Comando.Canal("noexiste"));

        // Assert: el error de ChatConfig (con la lista de válidos) y CERO llamadas
        _store.Mock.Verify(s => s.CambiarCanalAsync(It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("no existe"))), Times.Once);
    }

    [Test]
    public async Task CanalValido_CambiaElCanalSinAviso()
    {
        // Act: "daw" sin almohadilla vale (lo normaliza ChatConfig/hub)
        await _procesador.EjecutarAsync(new Comando.Canal("daw"));

        // Assert: la store recibe la orden; el éxito NO se avisa (el
        // historial del nuevo canal llega por evento y cambia la vista)
        _store.Mock.Verify(s => s.CambiarCanalAsync("daw"), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CanalValidoConErrorDeLaStore_AvisaElError()
    {
        // Arrange: la store traduce la caída de conexión a un mensaje
        _store.Mock.Setup(s => s.CambiarCanalAsync("daw"))
            .ReturnsAsync("Sin conexión con el servidor");

        // Act
        await _procesador.EjecutarAsync(new Comando.Canal("daw"));

        // Assert
        _store.Mock.Verify(s => s.Aviso("Sin conexión con el servidor"), Times.Once);
    }

    #endregion

    #region Nick Tests

    [Test]
    public async Task NickDemasiadoCorto_AvisaSinTocarLaRed()
    {
        // Act: un solo carácter no llega al mínimo de ChatConfig
        await _procesador.EjecutarAsync(new Comando.Nick("a"));

        // Assert: error de validación local, sin invocar al hub
        _store.Mock.Verify(s => s.CambiarNickAsync(It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("entre 2 y 20"))), Times.Once);
    }

    [Test]
    public async Task NickValido_CambiaElNickSinAvisoDeExito()
    {
        // Act
        await _procesador.EjecutarAsync(new Comando.Nick("Ana2"));

        // Assert: el "X ahora se llama Y" lo pinta el servidor, no aquí
        _store.Mock.Verify(s => s.CambiarNickAsync("Ana2"), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Usuarios Tests

    [Test]
    public async Task UsuariosSinArgumento_ListaLosDelCanalActualSinRed()
    {
        // Arrange: somos #general con dos personas dentro
        _store.CanalActual.OnNext("#general");
        _store.Usuarios.OnNext(
        [
            new UsuarioConectadoDto("Ana", DateTime.UtcNow),
            new UsuarioConectadoDto("Luis", DateTime.UtcNow)
        ]);

        // Act
        await _procesador.EjecutarAsync(new Comando.Usuarios());

        // Assert: lista local, sin consultar al servidor
        _store.Mock.Verify(s => s.ObtenerUsuariosAsync(It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("2 en línea en #general") &&
            t.Contains("● Ana") &&
            t.Contains("● Luis"))), Times.Once);
    }

    [Test]
    public async Task UsuariosDeOtroCanal_ConsultaAlServidor()
    {
        // Arrange: dentro de #general mirando dentro de #daw
        _store.CanalActual.OnNext("#general");
        _store.Mock.Setup(s => s.ObtenerUsuariosAsync("#daw"))
            .ReturnsAsync([new UsuarioConectadoDto("Luis", DateTime.UtcNow)]);

        // Act
        await _procesador.EjecutarAsync(new Comando.Usuarios("daw"));

        // Assert: se fue al servidor y la respuesta se pinta en el aviso
        _store.Mock.Verify(s => s.ObtenerUsuariosAsync("#daw"), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("1 en línea en #daw") && t.Contains("● Luis"))), Times.Once);
    }

    [Test]
    public async Task UsuariosDeOtroCanalSinConexion_AvisaSinMentir()
    {
        // Arrange: la store devuelve null cuando no pudo consultar
        _store.CanalActual.OnNext("#general");
        _store.Mock.Setup(s => s.ObtenerUsuariosAsync("#daw"))
            .ReturnsAsync((IReadOnlyList<UsuarioConectadoDto>?)null);

        // Act
        await _procesador.EjecutarAsync(new Comando.Usuarios("daw"));

        // Assert: el mismo mensaje vale para "canal desconocido" y "sin conexión"
        _store.Mock.Verify(s => s.Aviso("No pude consultar la lista de ese canal."), Times.Once);
    }

    [Test]
    public async Task UsuariosDeCanalInexistente_AvisaConLaListaDeValidos()
    {
        // Act
        await _procesador.EjecutarAsync(new Comando.Usuarios("noexiste"));

        // Assert: error local de ChatConfig, sin tocar la red
        _store.Mock.Verify(s => s.ObtenerUsuariosAsync(It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("no existe"))), Times.Once);
    }

    #endregion

    #region Privado Tests

    [Test]
    public async Task Privado_LlamaAlStoreConElDestinatarioYElTexto()
    {
        // Arrange
        _store.Mock.Setup(s => s.EnviarPrivadoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await _procesador.EjecutarAsync(new Comando.Privado("Ana", "¿nos vemos?"));

        // Assert
        _store.Mock.Verify(s => s.EnviarPrivadoAsync("Ana", "¿nos vemos?"), Times.Once);
    }

    [Test]
    public async Task PrivadoCorrecto_NoAvisaNada_NadieQuiereUnMensajeDeConfirmacion()
    {
        // Arrange: el eco del privado llega por evento y lo pinta la vista
        _store.Mock.Setup(s => s.EnviarPrivadoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await _procesador.EjecutarAsync(new Comando.Privado("Ana", "hola"));

        // Assert
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task PrivadoConError_DevuelveElAvisoAlFlujo()
    {
        // Arrange: el servidor no encuentra a nadie con ese nombre
        _store.Mock.Setup(s => s.EnviarPrivadoAsync("Maria", It.IsAny<string>()))
            .ReturnsAsync("No hay nadie llamado Maria conectado en tu canal");

        // Act
        await _procesador.EjecutarAsync(new Comando.Privado("Maria", "hola"));

        // Assert
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("Maria"))), Times.Once);
    }

    #endregion

    #region Reconectar Tests

    [Test]
    public async Task ReconectarDesconectado_VuelveAEntrarYAvisa()
    {
        // Arrange: fuera (la reconexión automática ya se rindió)
        _store.Estado.OnNext(EstadoConexion.Desconectado);
        _store.Mock.Setup(s => s.ReconectarAsync()).ReturnsAsync((string?)null);

        // Act
        await _procesador.EjecutarAsync(new Comando.Reconectar());

        // Assert
        _store.Mock.Verify(s => s.ReconectarAsync(), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("Reconectado"))), Times.Once);
    }

    [Test]
    public async Task ReconectarConectado_AvisaQueNoHaceFalta()
    {
        // Arrange: la store arranca conectada (StoreFalsa)
        // Act
        await _procesador.EjecutarAsync(new Comando.Reconectar());

        // Assert: ni reconectar ni mensajes de error
        _store.Mock.Verify(s => s.ReconectarAsync(), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("Ya estás conectado"))), Times.Once);
    }

    [Test]
    public async Task ReconectarMientrasSeReconectaSolo_PidePaciencia()
    {
        // Arrange: la reconexión automática de SignalR aún va en marcha
        _store.Estado.OnNext(EstadoConexion.Reconectando);

        // Act
        await _procesador.EjecutarAsync(new Comando.Reconectar());

        // Assert
        _store.Mock.Verify(s => s.ReconectarAsync(), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("sigue en marcha"))), Times.Once);
    }

    #endregion

    #region Sesión Tests

    [Test]
    public async Task SesionVer_MuestraLoGuardadoDelNavegador()
    {
        // Arrange
        _sesion.Setup(s => s.CargarAsync()).ReturnsAsync(new SesionGuardada("Ana", "#daw"));

        // Act
        await _procesador.EjecutarAsync(new Comando.Sesion(AccionSesion.Ver));

        // Assert
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("Ana") && t.Contains("#daw"))), Times.Once);
    }

    [Test]
    public async Task SesionVerSinNadaGuardado_LoDiceClaramente()
    {
        // Arrange: mock suelto → null (no hay sesión)
        _sesion.Setup(s => s.CargarAsync()).ReturnsAsync((SesionGuardada?)null);

        // Act
        await _procesador.EjecutarAsync(new Comando.Sesion(AccionSesion.Ver));

        // Assert
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("No hay ninguna sesión"))), Times.Once);
    }

    [Test]
    public async Task SesionBorrar_BorraYConfirmo()
    {
        // Arrange
        _sesion.Setup(s => s.BorrarAsync()).ReturnsAsync(true);

        // Act
        await _procesador.EjecutarAsync(new Comando.Sesion(AccionSesion.Borrar));

        // Assert
        _sesion.Verify(s => s.BorrarAsync(), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("Sesión borrada"))), Times.Once);
    }

    [Test]
    public async Task SesionBorrarSinNada_AvisaQueNoHabiaNada()
    {
        // Arrange
        _sesion.Setup(s => s.BorrarAsync()).ReturnsAsync(false);

        // Act
        await _procesador.EjecutarAsync(new Comando.Sesion(AccionSesion.Borrar));

        // Assert
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t => t.Contains("No había ninguna sesión"))), Times.Once);
    }

    #endregion

    #region Limpiar, Salir y errores Tests

    [Test]
    public async Task Limpiar_VaciaLaVistaLocalSinAvisar()
    {
        // Act: limpiar es silencioso, igual que en la terminal
        await _procesador.EjecutarAsync(new Comando.Limpiar());

        // Assert
        _store.Mock.Verify(s => s.LimpiarVista(), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Salir_CierraLaSesionOrdenadamente()
    {
        // Act: mismo camino que el botón Salir de la barra
        await _procesador.EjecutarAsync(new Comando.Salir());

        // Assert
        _store.Mock.Verify(s => s.CerrarAsync(), Times.Once);
    }

    [Test]
    public async Task Desconocido_RecuerdaLaAyuda()
    {
        // Act
        await _procesador.EjecutarAsync(new Comando.Desconocido("/magia"));

        // Assert: la palabra tal cual + el recordatorio de /ayuda
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("/magia") && t.Contains("/ayuda"))), Times.Once);
    }

    [Test]
    public async Task Invalido_EnseniaElUsoCorrecto()
    {
        // Act: el parser ya tipó el motivo y el uso (p. ej. /canal sin nombre)
        await _procesador.EjecutarAsync(new Comando.Invalido(
            "Falta el nombre del canal", "Usa: /canal <nombre> (p. ej. /canal daw)"));

        // Assert: las DOS partes llegan al usuario (motivo y ejemplo)
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("Falta el nombre del canal") &&
            t.Contains("Usa: /canal <nombre>"))), Times.Once);
    }

    [Test]
    public async Task MensajeNormal_NoPasaPorElProcesador()
    {
        // Act: la caja no manda aquí lo que no es comando; si llegara,
        // el procesador no hace nada con ello
        await _procesador.EjecutarAsync(new Comando.Enviar("hola"));

        // Assert: ni avisos ni cambios de estado
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.LimpiarVista(), Times.Never);
        _store.Mock.Verify(s => s.CerrarAsync(), Times.Never);
    }

    #endregion

    #region Reaccion Tests

    [Test]
    public async Task ReaccionAlUltimoMensaje_InvocaAlServerConSuId()
    {
        // Arrange: dos mensajes en la vista; el objetivo es el ÚLTIMO de persona
        var primero = new MensajeDto(Guid.NewGuid(), "Ana", "Hola", DateTime.UtcNow, TipoMensaje.Normal);
        var ultimo = new MensajeDto(Guid.NewGuid(), "Bruno", "¿Tal?", DateTime.UtcNow, TipoMensaje.Normal);
        _store.Mensajes.OnNext([primero, ultimo]);

        // Act
        await _procesador.EjecutarAsync(new Comando.Reaccion("👍"));

        // Assert: el mismo gesto que el chip del ratón, con el ID correcto
        _store.Mock.Verify(s => s.ReaccionarAsync(ultimo.Id, "👍"), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReaccionConEmojiFueraDeLaLista_SeAvisaSinTocarLaRed()
    {
        // Act: 🔥 es bonito, pero no está en la lista cerrada
        await _procesador.EjecutarAsync(new Comando.Reaccion("🔥"));

        // Assert: se explica el error Y se enseña la lista, sin invocar nada
        _store.Mock.Verify(s => s.ReaccionarAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("Emoji no admitido") && t.Contains("👍"))), Times.Once);
    }

    [Test]
    public async Task ReaccionSinMensajes_AvisaQueNoHayNadaQueReaccionar()
    {
        // Arrange: vista vacía (nunca se entró en un canal)
        _store.Mensajes.OnNext([]);

        // Act
        await _procesador.EjecutarAsync(new Comando.Reaccion("👍"));

        // Assert
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("No hay mensajes"))), Times.Once);
    }

    [Test]
    public async Task ReaccionASoloAvisosDeSistema_NoCuentaComoMensaje()
    {
        // Arrange: la vista solo tiene el aviso de unión (no es de persona)
        var aviso = new MensajeDto(
            Guid.NewGuid(), MensajesSistema.Autor, "Luis se ha unido al canal",
            DateTime.UtcNow, TipoMensaje.Sistema);
        _store.Mensajes.OnNext([aviso]);

        // Act
        await _procesador.EjecutarAsync(new Comando.Reaccion("👍"));

        // Assert: a los avisos no se les reacciona
        _store.Mock.Verify(s => s.ReaccionarAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("No hay mensajes"))), Times.Once);
    }

    [Test]
    public async Task ReaccionConErrorDelServer_SeAvisaEnLaVista()
    {
        // Arrange: el servidor rechaza (mensaje ya fuera de la caché)
        _store.Mensajes.OnNext(
            [new MensajeDto(Guid.NewGuid(), "Ana", "Hola", DateTime.UtcNow, TipoMensaje.Normal)]);
        _store.Mock.Setup(s => s.ReaccionarAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync("Ese mensaje ya no está en el canal");

        // Act
        await _procesador.EjecutarAsync(new Comando.Reaccion("👍"));

        // Assert
        _store.Mock.Verify(s => s.Aviso("Ese mensaje ya no está en el canal"), Times.Once);
    }

    #endregion

    #region Editar y borrar Tests

    [Test]
    public async Task EditarAccionTuyo_InvocaAlServerConSuId()
    {
        // Arrange: Ana escribe y ahora edita SU último mensaje — aunque
        // haya mensajes de otros (y de sistema) DESPUÉS en la vista
        _store.Nick.OnNext("Ana");
        _store.Mensajes.OnNext(
        [
            new MensajeDto(Guid.NewGuid(), "Ana", "Hola", DateTime.UtcNow, TipoMensaje.Normal),
            new MensajeDto(Guid.NewGuid(), "Luis", "¿Tal?", DateTime.UtcNow, TipoMensaje.Normal)
        ]);

        var mio = _store.Mensajes.Value[0];

        // Act
        await _procesador.EjecutarAsync(new Comando.Editar("Hola a todos"));

        // Assert: el objetivo es lo ÚLTIMO TUYO, no lo último del canal
        _store.Mock.Verify(s => s.EditarMensajeAsync(mio.Id, "Hola a todos"), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task EditarSinMensajesPropios_AvisaSinTocarLaRed()
    {
        // Arrange: en la vista solo hay mensajes de otros
        _store.Nick.OnNext("Ana");
        _store.Mensajes.OnNext(
            [new MensajeDto(Guid.NewGuid(), "Luis", "Hola", DateTime.UtcNow, TipoMensaje.Normal)]);

        // Act
        await _procesador.EjecutarAsync(new Comando.Editar("Corrijo una cosa"));

        // Assert
        _store.Mock.Verify(
            s => s.EditarMensajeAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("No hay ningún mensaje tuyo que editar"))), Times.Once);
    }

    [Test]
    public async Task EditarConErrorDelServer_SeAvisaEnLaVista()
    {
        // Arrange: el servidor rechaza (mensaje ya no en el canal)
        _store.Nick.OnNext("Ana");
        _store.Mensajes.OnNext(
            [new MensajeDto(Guid.NewGuid(), "Ana", "Hola", DateTime.UtcNow, TipoMensaje.Normal)]);
        _store.Mock.Setup(s => s.EditarMensajeAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync("Ese mensaje ya no está en el canal");

        // Act
        await _procesador.EjecutarAsync(new Comando.Editar("Corrijo una cosa"));

        // Assert
        _store.Mock.Verify(s => s.Aviso("Ese mensaje ya no está en el canal"), Times.Once);
    }

    [Test]
    public async Task BorrarAccionTuyo_InvocaAlServerConSuId()
    {
        // Arrange: mismo criterio que editar — tu último mensaje, no el del
        // canal
        _store.Nick.OnNext("Ana");
        _store.Mensajes.OnNext(
        [
            new MensajeDto(Guid.NewGuid(), "Ana", "Hola", DateTime.UtcNow, TipoMensaje.Normal),
            new MensajeDto(Guid.NewGuid(), "Luis", "¿Tal?", DateTime.UtcNow, TipoMensaje.Normal)
        ]);

        var mio = _store.Mensajes.Value[0];

        // Act
        await _procesador.EjecutarAsync(new Comando.Borrar());

        // Assert
        _store.Mock.Verify(s => s.BorrarMensajeAsync(mio.Id), Times.Once);
        _store.Mock.Verify(s => s.Aviso(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task BorrarSinMensajesPropios_AvisaSinTocarLaRed()
    {
        // Arrange: vista vacía (nunca se escribió nada)
        _store.Nick.OnNext("Ana");
        _store.Mensajes.OnNext([]);

        // Act
        await _procesador.EjecutarAsync(new Comando.Borrar());

        // Assert
        _store.Mock.Verify(s => s.BorrarMensajeAsync(It.IsAny<Guid>()), Times.Never);
        _store.Mock.Verify(s => s.Aviso(It.Is<string>(t =>
            t.Contains("No hay ningún mensaje tuyo que borrar"))), Times.Once);
    }

    [Test]
    public async Task BorrarConErrorDelServer_SeAvisaEnLaVista()
    {
        // Arrange: el servidor rechaza (mensaje ajeno, se cambió el nick...)
        _store.Nick.OnNext("Ana");
        _store.Mensajes.OnNext(
            [new MensajeDto(Guid.NewGuid(), "Ana", "Hola", DateTime.UtcNow, TipoMensaje.Normal)]);
        _store.Mock.Setup(s => s.BorrarMensajeAsync(It.IsAny<Guid>()))
            .ReturnsAsync("Solo puedes modificar tus propios mensajes");

        // Act
        await _procesador.EjecutarAsync(new Comando.Borrar());

        // Assert
        _store.Mock.Verify(s => s.Aviso("Solo puedes modificar tus propios mensajes"), Times.Once);
    }

    #endregion
}
