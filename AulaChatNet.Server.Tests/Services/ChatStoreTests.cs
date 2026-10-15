using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Tests.Services;

/// <summary>
/// Suite de tests para ChatStore, el estado en memoria del chat (Fase 1).
/// Verifica la unicidad de nombres (incluida la carrera concurrente), la
/// cola circular del historial y la limpieza al desconectar.
/// </summary>
[TestFixture]
public class ChatStoreTests
{
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
        var error = _store.AddUsuario("Ana", "conn-1");

        // Assert
        error.Should().BeNull();
        _store.GetUsuarioDeConexion("conn-1").Should().Be("Ana");
        _store.GetUsuarios().Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void AddUsuario_NombreRepetidoIgnorandoMayusculas_DevuelveErrorDuplicado()
    {
        // Arrange
        _store.AddUsuario("Ana", "conn-1");

        // Act
        var error = _store.AddUsuario("ANA", "conn-2");

        // Assert
        // "Ana" y "ANA" son el mismo nombre para el canal
        error.Should().Be(_config.ErrorUsuarioDuplicado);
        _store.GetUsuarios().Should().ContainSingle();
    }

    [Test]
    public void AddUsuario_MismaConexionDosVeces_DevuelveErrorDeConexion()
    {
        // Arrange
        _store.AddUsuario("Ana", "conn-1");

        // Act
        var error = _store.AddUsuario("Ana", "conn-1");

        // Assert
        error.Should().Be("Esta conexión ya está registrada en el canal");
    }

    [Test]
    public void AddUsuario_DiezCarrerasConElMismoNombre_SoloUnaGana()
    {
        // Arrange
        var resultados = new string?[10];

        // Act: diez conexiones intentan registrarse como "Ana" A LA VEZ
        Parallel.For(0, 10, i => resultados[i] = _store.AddUsuario("Ana", $"conn-{i}"));

        // Assert
        // El TryAdd del índice de nombres es atómico: exactamente una gana
        resultados.Count(r => r is null).Should().Be(1);
        resultados.Count(r => r is not null).Should().Be(9);
        _store.GetUsuarios().Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void AddUsuario_NombresDistintos_TodosSeRegistran()
    {
        // Act
        _store.AddUsuario("Ana", "conn-1");
        _store.AddUsuario("Luis", "conn-2");
        _store.AddUsuario("Marta", "conn-3");

        // Assert
        _store.GetUsuarios().Should().HaveCount(3);
    }

    #endregion

    #region RemoveUsuario Tests

    [Test]
    public void RemoveUsuario_ConexionRegistrada_DevuelveNombreYLimpiaLasListas()
    {
        // Arrange
        _store.AddUsuario("Ana", "conn-1");

        // Act
        var saliente = _store.RemoveUsuario("conn-1");

        // Assert
        saliente.Should().Be("Ana");
        _store.GetUsuarioDeConexion("conn-1").Should().BeNull();
        _store.GetUsuarios().Should().BeEmpty();
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
        _store.AddUsuario("Ana", "conn-1");
        _store.RemoveUsuario("conn-1");

        // Act: otra persona puede usar el nombre tras la salida
        var error = _store.AddUsuario("Ana", "conn-2");

        // Assert
        error.Should().BeNull();
        _store.GetUsuarios().Should().ContainSingle(u => u.Usuario == "Ana");
    }

    [Test]
    public void RemoveUsuario_SegundaLlamada_DevuelveNull()
    {
        // Arrange
        _store.AddUsuario("Ana", "conn-1");
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
        var historial = _store.GetHistorial();

        // Assert
        historial.Should().BeEmpty();
    }

    [Test]
    public void AddMensaje_SuperandoElLimiteConfigurado_DescartaElMasAntiguo()
    {
        // Arrange: límite pequeño para probar sin escribir mucho
        _config.HistorialMaximo = 3;

        // Act: cinco mensajes contra un límite de tres
        _store.AddMensaje(Mensaje("1"));
        _store.AddMensaje(Mensaje("2"));
        _store.AddMensaje(Mensaje("3"));
        _store.AddMensaje(Mensaje("4"));
        _store.AddMensaje(Mensaje("5"));

        // Assert: cola circular — salen los más antiguos, se queda el orden
        var historial = _store.GetHistorial();
        historial.Should().HaveCount(3);
        historial.Select(m => m.Texto).Should().Equal("3", "4", "5");
    }

    [Test]
    public void GetHistorial_DevuelveCopia_ModificarlaNoAfectaAlStore()
    {
        // Arrange
        _store.AddMensaje(Mensaje("hola"));
        var copia = (MensajeDto[])_store.GetHistorial();

        // Act: quien reciba el historial no debe poder tocar el estado interno
        copia[0] = Mensaje("manipulado");

        // Assert
        _store.GetHistorial()[0].Texto.Should().Be("hola");
    }

    #endregion

    #region GetUsuarios Tests

    [Test]
    public void GetUsuarios_VariosUsuarios_LosDevuelveOrdenadosPorNombre()
    {
        // Arrange
        _store.AddUsuario("carlos", "conn-1");
        _store.AddUsuario("Ana", "conn-2");
        _store.AddUsuario("Beatriz", "conn-3");

        // Act
        var usuarios = _store.GetUsuarios();

        // Assert: orden alfabético para el panel de "en línea"
        usuarios.Select(u => u.Usuario).Should().Equal("Ana", "Beatriz", "carlos");
    }

    #endregion
}
