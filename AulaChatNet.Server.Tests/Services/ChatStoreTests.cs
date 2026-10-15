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
}
