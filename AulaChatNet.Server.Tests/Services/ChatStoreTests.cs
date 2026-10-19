using AulaChatNet.Server.Models;
using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Tests.Services;

/// <summary>
/// Suite de tests para ChatStore, el estado en memoria de los canales (Fase 1).
/// Verifica la unicidad de nombres POR CANAL (incluida la carrera concurrente),
/// los historiales independientes, el cambio de canal y la limpieza al salir.
/// </summary>
[TestFixture]
public class ChatStoreTests
{
    private const string General = "#general";
    private const string Smr = "#smr";

    private ChatConfig _config = null!;
    private ChatStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _config = new ChatConfig();
        _store = new ChatStore(Options.Create(_config));
    }

    /// <summary>Crea un mensaje de prueba con los valores dados.</summary>
    private static MensajeDto Mensaje(string texto) =>
        new(Guid.NewGuid(), "Ana", texto, DateTime.UtcNow, TipoMensaje.Normal);

    #region AddUsuario Tests

    [Test]
    public void AddUsuario_NombreValido_DevuelveNullYRegistraElUsuario()
    {
        // Act
        var error = _store.AddUsuario(General, "Ana", "conn-1");

        // Assert
        error.Should().BeNull();
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
        _store.GetUsuarios(General).Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void AddUsuario_NombreRepetidoEnElMismoCanal_DevuelveErrorDuplicado()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act
        var error = _store.AddUsuario(General, "ANA", "conn-2");

        // Assert
        // "Ana" y "ANA" son el mismo nombre DENTRO del canal
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetUsuarios(General).Should().ContainSingle();
    }

    [Test]
    public void AddUsuario_MismoNombreEnDistintosCanales_AmbosSeRegistran()
    {
        // Act: en #general y en #smr puede haber una "Ana" distinta
        var errorGeneral = _store.AddUsuario(General, "Ana", "conn-1");
        var errorSmr = _store.AddUsuario(Smr, "Ana", "conn-2");

        // Assert
        errorGeneral.Should().BeNull();
        errorSmr.Should().BeNull();
        _store.GetUsuarios(General).Should().ContainSingle(u => u.Usuario == "Ana");
        _store.GetUsuarios(Smr).Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void AddUsuario_MismaConexionDosVeces_DevuelveErrorDeConexion()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act
        var error = _store.AddUsuario(General, "Ana", "conn-1");

        // Assert
        error.Should().Be("Esta conexión ya está registrada en el canal");
    }

    [Test]
    public void AddUsuario_DiezCarrerasConElMismoNombre_SoloUnaGana()
    {
        // Arrange
        var resultados = new string?[10];

        // Act: diez conexiones intentan registrarse como "Ana" A LA VEZ
        Parallel.For(0, 10, i => resultados[i] = _store.AddUsuario(General, "Ana", $"conn-{i}"));

        // Assert
        // El TryAdd del índice de nombres es atómico: exactamente una gana
        resultados.Count(r => r is null).Should().Be(1);
        resultados.Count(r => r is not null).Should().Be(9);
        _store.GetUsuarios(General).Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void AddUsuario_NombresDistintos_TodosSeRegistran()
    {
        // Act
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.AddUsuario(General, "Luis", "conn-2");
        _store.AddUsuario(General, "Marta", "conn-3");

        // Assert
        _store.GetUsuarios(General).Should().HaveCount(3);
    }

    #endregion

    #region CambiarCanal Tests

    [Test]
    public void CambiarCanal_UsuarioActivo_SeMueveYLiberaElNombreEnElAnterior()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act
        var error = _store.CambiarCanal("conn-1", Smr);

        // Assert
        error.Should().BeNull();
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(Smr, "Ana"));
        _store.GetUsuarios(General).Should().BeEmpty();
        _store.GetUsuarios(Smr).Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void CambiarCanal_NombreOcupadoEnElDestino_DevuelveErrorYSeQuedaDondeEstaba()
    {
        // Arrange: otra "Ana" ya vive en #smr
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.AddUsuario(Smr, "Ana", "conn-2");

        // Act
        var error = _store.CambiarCanal("conn-1", Smr);

        // Assert: rechazado, se queda en #general sin tocar nada de #smr
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
        _store.GetUsuarios(General).Should().ContainSingle();
        _store.GetUsuarios(Smr).Should().ContainSingle();
    }

    [Test]
    public void CambiarCanal_AlMismoCanal_DevuelveNullSinCambios()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act
        var error = _store.CambiarCanal("conn-1", General);

        // Assert
        error.Should().BeNull();
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
    }

    [Test]
    public void CambiarCanal_ConexionNoRegistrada_DevuelveError()
    {
        // Act
        var error = _store.CambiarCanal("conn-fantasma", Smr);

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
    }

    #endregion

    #region CambiarNick Tests

    [Test]
    public void CambiarNick_NombreValido_CambiaElNombreYConservaLaEntrada()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");
        var antes = _store.GetUsuarios(General)[0];

        // Act
        var error = _store.CambiarNick("conn-1", "Ana2");

        // Assert
        error.Should().BeNull();
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana2"));
        _store.GetUsuarios(General).Select(u => u.Usuario).Should().Equal("Ana2");

        // La hora de entrada de la sesión NO se pierde al cambiar de nombre
        _store.GetUsuarios(General)[0].ConectadoDesde.Should().Be(antes.ConectadoDesde);
    }

    [Test]
    public void CambiarNick_NombreOcupadoEnElMismoCanal_DevuelveErrorYSeQuedaConElViejo()
    {
        // Arrange: Luis ocupa ese nombre en #general
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.AddUsuario(General, "Luis", "conn-2");

        // Act
        var error = _store.CambiarNick("conn-1", "Luis");

        // Assert: rechazado y sin tocar nada
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
        _store.GetUsuarios(General).Select(u => u.Usuario).Should().BeEquivalentTo("Ana", "Luis");
    }

    [Test]
    public void CambiarNick_NombreLibreTrasSalir_PuedeUsarlo()
    {
        // Arrange: Luis se va del canal y su nombre queda libre
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.AddUsuario(General, "Luis", "conn-2");
        _store.RemoveUsuario("conn-2");

        // Act
        var error = _store.CambiarNick("conn-1", "Luis");

        // Assert
        error.Should().BeNull();
        _store.GetUsuarios(General).Select(u => u.Usuario).Should().Equal("Luis");
    }

    [Test]
    public void CambiarNick_AlMismoNombre_DevuelveNullSinCambios()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act: mayúsculas distintas, mismo nombre
        var error = _store.CambiarNick("conn-1", "ANA");

        // Assert
        error.Should().BeNull();
        _store.GetConexion("conn-1").Should().Be(new ConexionChat(General, "Ana"));
    }

    [Test]
    public void CambiarNick_ConexionNoRegistrada_DevuelveError()
    {
        // Act
        var error = _store.CambiarNick("conn-fantasma", "Ana2");

        // Assert
        error.Should().Be("No estás registrado en el canal, vuelve a conectarte");
    }

    #endregion

    #region RemoveUsuario Tests

    [Test]
    public void RemoveUsuario_ConexionRegistrada_DevuelveCanalYNombreYLimpia()
    {
        // Arrange
        _store.AddUsuario(Smr, "Ana", "conn-1");

        // Act
        var saliente = _store.RemoveUsuario("conn-1");

        // Assert: el hub necesita el CANAL para avisar solo a ese grupo
        saliente.Should().Be(new ConexionChat(Smr, "Ana"));
        _store.GetConexion("conn-1").Should().BeNull();
        _store.GetUsuarios(Smr).Should().BeEmpty();
    }

    [Test]
    public void RemoveUsuario_ConexionNoRegistrada_DevuelveNull()
    {
        // Act
        var saliente = _store.RemoveUsuario("conn-desconocida");

        // Assert
        saliente.Should().BeNull();
    }

    [Test]
    public void RemoveUsuario_NombreLiberado_PuedeVolverARegistrarse()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.RemoveUsuario("conn-1");

        // Act: otra persona puede usar el nombre tras la salida
        var error = _store.AddUsuario(General, "Ana", "conn-2");

        // Assert
        error.Should().BeNull();
        _store.GetUsuarios(General).Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void RemoveUsuario_SegundaLlamada_DevuelveNull()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.RemoveUsuario("conn-1");

        // Act
        var segunda = _store.RemoveUsuario("conn-1");

        // Assert
        segunda.Should().BeNull();
    }

    #endregion

    #region AddMensaje / GetHistorial Tests

    [Test]
    public void GetHistorial_TiendaVacia_DevuelveListaVacia()
    {
        // Act
        var historial = _store.GetHistorial(General);

        // Assert
        historial.Should().BeEmpty();
    }

    [Test]
    public void GetHistorial_CanalSinMensajes_DevuelveVacioSinCrearEstado()
    {
        // Act: un canal del que nadie ha escrito aún
        var historial = _store.GetHistorial("#daw");

        // Assert
        historial.Should().BeEmpty();
    }

    [Test]
    public void AddMensaje_SuperandoElLimiteConfigurado_DescartaElMasAntiguo()
    {
        // Arrange: límite pequeño para probar sin escribir mucho
        _config.HistorialMaximo = 3;

        // Act: cinco mensajes contra un límite de tres
        _store.AddMensaje(General, Mensaje("1"));
        _store.AddMensaje(General, Mensaje("2"));
        _store.AddMensaje(General, Mensaje("3"));
        _store.AddMensaje(General, Mensaje("4"));
        _store.AddMensaje(General, Mensaje("5"));

        // Assert: cola circular — salen los más antiguos, se queda el orden
        var historial = _store.GetHistorial(General);
        historial.Should().HaveCount(3);
        historial.Select(m => m.Texto).Should().Equal("3", "4", "5");
    }

    [Test]
    public void AddMensaje_DosCanales_HistorialesIndependientes()
    {
        // Act
        _store.AddMensaje(General, Mensaje("hola en general"));
        _store.AddMensaje(Smr, Mensaje("hola en smr"));

        // Assert
        _store.GetHistorial(General).Should().ContainSingle(m => m.Texto == "hola en general");
        _store.GetHistorial(Smr).Should().ContainSingle(m => m.Texto == "hola en smr");
    }

    [Test]
    public void GetHistorial_DevuelveCopia_ModificarlaNoAfectaAlStore()
    {
        // Arrange
        _store.AddMensaje(General, Mensaje("hola"));
        var copia = (MensajeDto[])_store.GetHistorial(General);

        // Act: quien reciba el historial no debe poder tocar el estado interno
        copia[0] = Mensaje("manipulado");

        // Assert
        _store.GetHistorial(General)[0].Texto.Should().Be("hola");
    }

    #endregion

    #region GetUsuarios Tests

    [Test]
    public void GetUsuarios_VariosUsuarios_LosDevuelveOrdenadosPorNombre()
    {
        // Arrange
        _store.AddUsuario(General, "carlos", "conn-1");
        _store.AddUsuario(General, "Ana", "conn-2");
        _store.AddUsuario(General, "Beatriz", "conn-3");

        // Act
        var usuarios = _store.GetUsuarios(General);

        // Assert: orden alfabético para el panel de "en línea"
        usuarios.Select(u => u.Usuario).Should().Equal("Ana", "Beatriz", "carlos");
    }

    [Test]
    public void GetUsuarios_DosCanales_ListasIndependientes()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.AddUsuario(Smr, "Luis", "conn-2");

        // Act & Assert
        _store.GetUsuarios(General).Should().ContainSingle(u => u.Usuario == "Ana");
        _store.GetUsuarios(Smr).Should().ContainSingle(u => u.Usuario == "Luis");
    }

    #endregion

    #region GetEstadisticas Tests

    [Test]
    public void GetEstadisticas_SinNadie_DevuelveCero()
    {
        // Act
        var est = _store.GetEstadisticas();

        // Assert
        est.TotalConectados.Should().Be(0);
        est.Canales.Should().BeEmpty();
    }

    [Test]
    public void GetEstadisticas_VariosUsuarios_CuentaTotalYPorSala()
    {
        // Arrange: 3 en #general y 1 en #smr
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.AddUsuario(General, "Luis", "conn-2");
        _store.AddUsuario(General, "Marta", "conn-3");
        _store.AddUsuario(Smr, "Carlos", "conn-4");

        // Act
        var est = _store.GetEstadisticas();

        // Assert
        est.TotalConectados.Should().Be(4);
        est.Canales.Should().ContainSingle(c => c.Canal == General && c.Conectados == 3);
        est.Canales.Should().ContainSingle(c => c.Canal == Smr && c.Conectados == 1);
    }

    [Test]
    public void GetEstadisticas_SalaVacia_NoApareceEnElDesglose()
    {
        // Arrange: alguien entró y salió de #smr; solo queda Ana en #general
        _store.AddUsuario(Smr, "Luis", "conn-1");
        _store.RemoveUsuario("conn-1");
        _store.AddUsuario(General, "Ana", "conn-2");

        // Act
        var est = _store.GetEstadisticas();

        // Assert: las salas vacías no se emiten (el cliente las pinta con 0)
        est.TotalConectados.Should().Be(1);
        est.Canales.Should().ContainSingle().Which.Canal.Should().Be(General);
    }

    [Test]
    public void GetEstadisticas_OrdenadoPorNumeroDeConectados()
    {
        // Arrange: #smr con 2, #general con 1
        _store.AddUsuario(Smr, "Ana", "conn-1");
        _store.AddUsuario(Smr, "Luis", "conn-2");
        _store.AddUsuario(General, "Marta", "conn-3");

        // Act
        var est = _store.GetEstadisticas();

        // Assert: la sala más poblada primero
        est.Canales.Select(c => c.Canal).Should().Equal(Smr, General);
    }

    #endregion

    #region GetConnectionId Tests

    [Test]
    public void GetConnectionId_UsuarioConectado_DevuelveSuConexion()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act: el paso inverso de GetConexion, para los mensajes privados
        var connectionId = _store.GetConnectionId(General, "Ana");

        // Assert
        connectionId.Should().Be("conn-1");
    }

    [Test]
    [TestCase("ana")]
    [TestCase("ANA")]
    [TestCase("  Ana  ")]
    public void GetConnectionId_NombreConOtrasMayusculasOEspacios_DevuelveLaMismaConexion(string buscado)
    {
        // Arrange: el store normaliza al registrar, y debe normalizar también
        // al buscar — si no, "ANA" y "Ana" serían dos personas distintas
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act
        var connectionId = _store.GetConnectionId(General, buscado);

        // Assert
        connectionId.Should().Be("conn-1");
    }

    [Test]
    public void GetConnectionId_EnOtroCanal_DevuelveNull()
    {
        // Arrange: Ana está en #general, no en #smr
        _store.AddUsuario(General, "Ana", "conn-1");

        // Act
        var connectionId = _store.GetConnectionId(Smr, "Ana");

        // Assert: los nombres solo identifican dentro de SU sala
        connectionId.Should().BeNull();
    }

    [Test]
    public void GetConnectionId_DosHomonimosEnDistintosCanales_DevuelveElDelCanalDado()
    {
        // Arrange
        _store.AddUsuario(General, "Luis", "conn-general");
        _store.AddUsuario(Smr, "Luis", "conn-smr");

        // Act
        var enGeneral = _store.GetConnectionId(General, "Luis");
        var enSmr = _store.GetConnectionId(Smr, "Luis");

        // Assert: cada Luis es una persona distinta, y el canal lo decide
        enGeneral.Should().Be("conn-general");
        enSmr.Should().Be("conn-smr");
    }

    [Test]
    public void GetConnectionId_UsuarioNoConectado_DevuelveNull()
    {
        // Act
        var connectionId = _store.GetConnectionId(General, "Maria");

        // Assert
        connectionId.Should().BeNull();
    }

    [Test]
    public void GetConnectionId_ConexionDadaDeBaja_YaNoAparece()
    {
        // Arrange
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.RemoveUsuario("conn-1");

        // Act: nadie puede escribirle a alguien que ya salió
        var connectionId = _store.GetConnectionId(General, "Ana");

        // Assert
        connectionId.Should().BeNull();
    }

    #endregion

    #region Reacciones Tests

    [Test]
    public void AlternarReaccion_MensajeNormal_AñadeElChipConLaPersona()
    {
        // Arrange
        var mensaje = Mensaje("Buenas a todos");
        _store.AddMensaje(General, mensaje);

        // Act
        var estado = _store.AlternarReaccion(General, mensaje.Id, "👍", "Ana");

        // Assert: un chip con una persona, apuntando al mensaje correcto
        estado.Should().NotBeNull();
        estado!.IdMensaje.Should().Be(mensaje.Id);
        var chip = estado.Reacciones.Should().ContainSingle().Subject;
        chip.Emoji.Should().Be("👍");
        chip.Personas.Should().Equal("Ana");
    }

    [Test]
    public void AlternarReaccion_MismaPersonaOtraVez_QuitaLaReaccion()
    {
        // Arrange: es un INTERRUPTOR, no un contador doble
        var mensaje = Mensaje("Buenas a todos");
        _store.AddMensaje(General, mensaje);
        _store.AlternarReaccion(General, mensaje.Id, "👍", "Ana");

        // Act
        var estado = _store.AlternarReaccion(General, mensaje.Id, "👍", "Ana");

        // Assert: lista vacía = "este mensaje ya no tiene reacciones"
        estado.Should().NotBeNull();
        estado!.Reacciones.Should().BeEmpty();
    }

    [Test]
    public void AlternarReaccion_DosPersonasElMismoEmoji_UnSoloChipConLosDosNombres()
    {
        // Arrange
        var mensaje = Mensaje("Buenas a todos");
        _store.AddMensaje(General, mensaje);
        _store.AlternarReaccion(General, mensaje.Id, "👍", "Bruno");

        // Act: Ana reacciona después de Bruno
        var estado = _store.AlternarReaccion(General, mensaje.Id, "👍", "Ana");

        // Assert: UN chip con dos personas, en orden alfabético
        estado!.Reacciones.Should().ContainSingle()
            .Which.Personas.Should().Equal("Ana", "Bruno");
    }

    [Test]
    public void AlternarReaccion_DosPersonasYUnaSeDeshace_SigueQuedandoLaOtra()
    {
        // Arrange: chip con DOS personas
        var mensaje = Mensaje("Buenas a todos");
        _store.AddMensaje(General, mensaje);
        _store.AlternarReaccion(General, mensaje.Id, "😂", "Ana");
        _store.AlternarReaccion(General, mensaje.Id, "😂", "Luis");

        // Act: Ana se deshace de la SUYA
        var estado = _store.AlternarReaccion(General, mensaje.Id, "😂", "Ana");

        // Assert: el chip SOBREVIVE con Luis. Regresión de un bug real: el
        // borrado del emoji solo comprobaba que el diccionario siguiera siendo
        // el mismo, no que estuviera vacío, así que el 😂 entero se destruía y
        // la reacción de Luis desaparecía de todas las pantallas — salió al
        // probar la web con dos pestañas y NUNCA en los tests
        estado.Should().NotBeNull();
        estado!.Reacciones.Should().ContainSingle()
            .Which.Personas.Should().Equal("Luis");
    }

    [Test]
    public void AlternarReaccion_MismoNombreConOtrasMayusculas_EsLaMismaPersona()
    {
        // Arrange: la unicidad de nombres es por canal, y el store normaliza
        var mensaje = Mensaje("Buenas a todos");
        _store.AddMensaje(General, mensaje);
        _store.AlternarReaccion(General, mensaje.Id, "👍", "Ana");

        // Act: "ANA" es la misma persona, así que QUITA lo que puso "Ana"
        var estado = _store.AlternarReaccion(General, mensaje.Id, "👍", "ANA");

        // Assert
        estado!.Reacciones.Should().BeEmpty();
    }

    [Test]
    public void AlternarReaccion_EmojisDistintos_DosChipsEnElOrdenDeLaLista()
    {
        // Arrange: primero el 😂, que en la lista cerrada va DESPUÉS del 👍
        var mensaje = Mensaje("Buenas a todos");
        _store.AddMensaje(General, mensaje);
        _store.AlternarReaccion(General, mensaje.Id, "😂", "Ana");

        // Act
        var estado = _store.AlternarReaccion(General, mensaje.Id, "👍", "Bruno");

        // Assert: los chips se pintan en el orden FIJO de la lista, no en el
        // en que se pusieron — así no "baile" al añadir uno nuevo
        estado!.Reacciones.Select(r => r.Emoji).Should().Equal("👍", "😂");
    }

    [Test]
    public void AlternarReaccion_MensajeInexistente_DevuelveNull()
    {
        // Act: un Guid que nadie ha escrito jamás
        var estado = _store.AlternarReaccion(General, Guid.NewGuid(), "👍", "Ana");

        // Assert: null = "ese mensaje no está en el canal"
        estado.Should().BeNull();
    }

    [Test]
    public void AlternarReaccion_MensajeDeSistema_DevuelveNull()
    {
        // Arrange: los avisos ("Ana se ha unido") no son mensajes a los que
        // se pueda reaccionar
        var aviso = new MensajeDto(Guid.NewGuid(), "Ana", "Ana se ha unido al canal", DateTime.UtcNow, TipoMensaje.Sistema);
        _store.AddMensaje(General, aviso);

        // Act
        var estado = _store.AlternarReaccion(General, aviso.Id, "👍", "Bruno");

        // Assert
        estado.Should().BeNull();
    }

    [Test]
    public void AlternarReaccion_MensajeDeOtroCanal_DevuelveNull()
    {
        // Arrange: el mensaje es de #smr...
        var mensaje = Mensaje("Hola desde smr");
        _store.AddMensaje(Smr, mensaje);

        // Act: ...y alguien de #general intenta reaccionarle
        var estado = _store.AlternarReaccion(General, mensaje.Id, "👍", "Ana");

        // Assert: nadie reacciona a salas ajenas
        estado.Should().BeNull();
    }

    [Test]
    public void AlternarReaccion_CuandoElMensajeSaleDeLaCache_SeBorranSusReacciones()
    {
        // Arrange: caché de 3 mensajes; el primero se irá al llegar el cuarto
        _config.HistorialMaximo = 3;
        var primero = Mensaje("El primero");
        _store.AddMensaje(General, primero);
        _store.AlternarReaccion(General, primero.Id, "👍", "Ana");

        // Act: se desborda la caché circular
        _store.AddMensaje(General, Mensaje("Segundo"));
        _store.AddMensaje(General, Mensaje("Tercero"));
        _store.AddMensaje(General, Mensaje("Cuarto"));

        // Assert: lo que se fue del historial se lleva SUS reacciones —
        // una reacción sobre un mensaje que ya nadie ve es basura
        _store.GetReacciones(General).Should().BeEmpty();
        _store.AlternarReaccion(General, primero.Id, "👍", "Ana").Should().BeNull();
    }

    [Test]
    public void GetReacciones_SinNada_DevuelveVacio()
    {
        // Act
        var reacciones = _store.GetReacciones(General);

        // Assert: sin excepción — quien lo llama ya sabe avisar
        reacciones.Should().BeEmpty();
    }

    [Test]
    public void GetReacciones_SoloDevuelveMensajesConAlMenosUnChip()
    {
        // Arrange: dos mensajes, pero solo uno con reacciones
        var conReaccion = Mensaje("Este gusta");
        _store.AddMensaje(General, conReaccion);
        _store.AddMensaje(General, Mensaje("A este, nadie"));
        _store.AlternarReaccion(General, conReaccion.Id, "😂", "Ana");

        // Act
        var reacciones = _store.GetReacciones(General);

        // Assert: el snapshot es "un DTO por mensaje con chip", nada más
        reacciones.Should().ContainSingle()
            .Which.IdMensaje.Should().Be(conReaccion.Id);
    }

    [Test]
    public void GetReacciones_ElNombreSeConservaAunqueDespuesCambieDeApodo()
    {
        // Arrange: sin autenticación no hay identidad a la que reescribir,
        // y disimularlo sería mentir: la reacción lleva el nombre del momento
        var mensaje = Mensaje("Buenas a todos");
        _store.AddUsuario(General, "Ana", "conn-1");
        _store.AddMensaje(General, mensaje);
        _store.AlternarReaccion(General, mensaje.Id, "👍", "Ana");

        // Act: Ana se renombra DESPUÉS de haber reaccionado
        _store.CambiarNick("conn-1", "Anita");

        // Assert
        _store.GetReacciones(General)[0].Reacciones[0].Personas.Should().Equal("Ana");
    }

    #endregion
}
