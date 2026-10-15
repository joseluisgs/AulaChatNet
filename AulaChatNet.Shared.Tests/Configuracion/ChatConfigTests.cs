using FluentAssertions;
using AulaChatNet.Shared.Configuracion;

namespace AulaChatNet.Shared.Tests.Configuracion;

/// <summary>
/// Suite de tests para ChatConfig.
/// Es la validación compartida por servidor y clientes: si se rompe aquí,
/// se rompe en todo el sistema. Verifica defaults, reglas de validación
/// y que los mensajes de error reflejan los límites configurados.
/// </summary>
[TestFixture]
public class ChatConfigTests
{
    private ChatConfig _config = null!;

    [SetUp]
    public void Setup() => _config = new ChatConfig();

    #region Valores por defecto Tests

    [Test]
    public void Constructor_ValoresPorDefecto_DevuelveLimitesDeFase1()
    {
        // Assert
        _config.Canal.Should().Be("#general");
        _config.UsuarioLongitudMinima.Should().Be(2);
        _config.UsuarioLongitudMaxima.Should().Be(20);
        _config.MensajeLongitudMaxima.Should().Be(500);
        _config.HistorialMaximo.Should().Be(25);
        _config.AvatarBaseUrl.Should().Be("https://api.dicebear.com/10.x");
    }

    #endregion

    #region ValidarUsuario Tests

    [Test]
    public void ValidarUsuario_Nulo_RetornaErrorVacio()
    {
        // Act
        var error = _config.ValidarUsuario(null);

        // Assert
        error.Should().Be(_config.ErrorUsuarioVacio);
    }

    [Test]
    public void ValidarUsuario_Vacio_RetornaErrorVacio()
    {
        // Act
        var error = _config.ValidarUsuario("");

        // Assert
        error.Should().Be(_config.ErrorUsuarioVacio);
    }

    [Test]
    public void ValidarUsuario_SoloEspacios_RetornaErrorVacio()
    {
        // Act
        var error = _config.ValidarUsuario("   ");

        // Assert
        error.Should().Be(_config.ErrorUsuarioVacio);
    }

    [Test]
    public void ValidarUsuario_UnSoloCaracter_RetornaErrorLongitud()
    {
        // Act
        var error = _config.ValidarUsuario("A");

        // Assert
        error.Should().Be(_config.ErrorUsuarioLongitud);
    }

    [Test]
    public void ValidarUsuario_VeintiUnCaracteres_RetornaErrorLongitud()
    {
        // Arrange
        var usuarioLargo = new string('a', 21);

        // Act
        var error = _config.ValidarUsuario(usuarioLargo);

        // Assert
        error.Should().Be(_config.ErrorUsuarioLongitud);
    }

    [Test]
    public void ValidarUsuario_NombreValido_RetornaNull()
    {
        // Act
        var error = _config.ValidarUsuario("Ana");

        // Assert
        error.Should().BeNull();
    }

    #endregion

    #region ValidarMensaje Tests

    [Test]
    public void ValidarMensaje_Nulo_RetornaErrorVacio()
    {
        // Act
        var error = _config.ValidarMensaje(null);

        // Assert
        error.Should().Be(_config.ErrorMensajeVacio);
    }

    [Test]
    public void ValidarMensaje_Vacio_RetornaErrorVacio()
    {
        // Act
        var error = _config.ValidarMensaje("");

        // Assert
        error.Should().Be(_config.ErrorMensajeVacio);
    }

    [Test]
    public void ValidarMensaje_DeQuinientosUnCaracteres_RetornaErrorLongitud()
    {
        // Arrange
        var textoLargo = new string('a', 501);

        // Act
        var error = _config.ValidarMensaje(textoLargo);

        // Assert
        error.Should().Be(_config.ErrorMensajeLongitud);
    }

    [Test]
    public void ValidarMensaje_MensajeValido_RetornaNull()
    {
        // Act
        var error = _config.ValidarMensaje("Buenas, ¿arrancamos?");

        // Assert
        error.Should().BeNull();
    }

    #endregion

    #region Mensajes computados desde la configuración Tests

    [Test]
    public void ErrorUsuarioLongitud_SiCambiaElLimite_ElMensajeReflejaElNuevoLimite()
    {
        // Arrange
        _config.UsuarioLongitudMaxima = 30;

        // Act
        var error = _config.ErrorUsuarioLongitud;

        // Assert
        // Si el límite cambia por config, el mensaje de error no puede quedar desfasado
        error.Should().Contain("30");
    }

    [Test]
    public void ErrorMensajeLongitud_SiCambiaElLimite_ElMensajeReflejaElNuevoLimite()
    {
        // Arrange
        _config.MensajeLongitudMaxima = 100;

        // Act
        var error = _config.ErrorMensajeLongitud;

        // Assert
        error.Should().Contain("100");
    }

    [Test]
    public void ValidarUsuario_LimiteMinimoTres_CaracteresDosEsInvalido()
    {
        // Arrange
        _config.UsuarioLongitudMinima = 3;

        // Act
        var error = _config.ValidarUsuario("An");

        // Assert
        error.Should().Be(_config.ErrorUsuarioLongitud);
    }

    #endregion
}
