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

    /// <summary>
    /// Puebla la lista de canales como haría el ConfigurationBinder con el
    /// appsettings de Fase 1 (el binder AÑADE, por eso el default está vacío).
    /// </summary>
    private void PonerCanalesDeFase1() =>
        _config.Canales.AddRange(["#general", "#smr", "#daw", "#asir", "#dam"]);

    #region Valores por defecto Tests

    [Test]
    public void Constructor_CanalesVacios_ListaPreparadaParaElBinder()
    {
        // Assert
        // La lista arranca vacía: el ConfigurationBinder AÑADE lo de
        // appsettings (si tuviera defaults, se duplicarían con los del fichero)
        _config.Canales.Should().BeEmpty();
        _config.CanalPorDefecto.Should().Be("#general");
        _config.HubRuta.Should().Be("/hubs/chat");
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

    #region Canales Tests

    [Test]
    [TestCase("#general")]
    [TestCase("#smr")]
    [TestCase("#SMR")]      // insensible a mayúsculas
    [TestCase("smr")]       // sin almohadilla también vale
    [TestCase("  #daw  ")]  // con espacios alrededor
    public void EsCanalValido_CanalDeLaLista_RetornaTrue(string canal)
    {
        // Arrange
        PonerCanalesDeFase1();

        // Act
        var valido = _config.EsCanalValido(canal);

        // Assert
        valido.Should().BeTrue();
    }

    [Test]
    [TestCase("#facil")]    // no está en la lista
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void EsCanalValido_CanalDesconocido_RetornaFalse(string? canal)
    {
        // Arrange
        PonerCanalesDeFase1();

        // Act
        var valido = _config.EsCanalValido(canal);

        // Assert
        valido.Should().BeFalse();
    }

    [Test]
    public void EncontrarCanal_SinAlmohadilla_RetornaLaFormaCanonica()
    {
        // Arrange
        PonerCanalesDeFase1();

        // Act
        var canonico = _config.EncontrarCanal("smr");

        // Assert
        canonico.Should().Be("#smr");
    }

    [Test]
    public void EncontrarCanal_CanalDesconocido_RetornaNull()
    {
        // Act
        var canonico = _config.EncontrarCanal("#otra");

        // Assert
        canonico.Should().BeNull();
    }

    [Test]
    public void ErrorCanalDesconocido_ListaTodosLosCanalesConfigurados()
    {
        // Arrange
        PonerCanalesDeFase1();

        // Act
        var error = _config.ErrorCanalDesconocido("#otra");

        // Assert
        error.Should().Contain("#otra").And.Contain("#smr").And.Contain("#dam");
    }

    [Test]
    public void CanalPorDefecto_SiLaListaEstaVacia_RetornaElDeSeguridad()
    {
        // Arrange
        _config.Canales.Clear();

        // Act
        var defecto = _config.CanalPorDefecto;

        // Assert: red de seguridad, como los defaults de la clase
        defecto.Should().Be("#general");
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

    #region Errores de privado Tests

    [Test]
    public void ErrorPrivadoDestinoVacio_DiceQueFaltaAQuienVa()
    {
        // Assert: es el texto que verá quien invoque el hub a pelo, sin pasar
        // por el parser (que ya habría rechazado la línea en el cliente)
        _config.ErrorPrivadoDestinoVacio.Should().Contain("destinatario");
    }

    [Test]
    public void ErrorPrivadoAUnoMismo_NoSeTrataDeUnErrorDeRed()
    {
        // Assert: palabras de usuario, como todo lo que sale en pantalla
        _config.ErrorPrivadoAUnoMismo.Should().Contain("ti mismo");
    }

    [Test]
    public void ErrorPrivadoSinDestino_NombraAlQueSeBuscaba()
    {
        // Act
        var error = _config.ErrorPrivadoSinDestino("Maria");

        // Assert: el nombre va dentro para que el usuario sepa QUÉ escribió mal
        error.Should().Contain("Maria");
    }

    #endregion
}
