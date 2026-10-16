using FluentAssertions;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Suite de tests para MensajesSistema.
/// Son los mensajes por los que los clientes se enteran de las entradas
/// Y las desconexiones ("X ha salido del canal"), viajando por el mismo
/// flujo que los mensajes normales.
/// </summary>
[TestFixture]
public class MensajesSistemaTests
{
    #region UsuarioUnido Tests

    [Test]
    public void UsuarioUnido_UsuarioAna_TextoEsperado()
    {
        // Act
        var mensaje = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje.Texto.Should().Be("Ana se ha unido al canal");
    }

    [Test]
    public void UsuarioUnido_TipoSistema_YAutorSistema()
    {
        // Act
        var mensaje = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);
        mensaje.Usuario.Should().Be(MensajesSistema.Autor);
    }

    #endregion

    #region UsuarioSalio Tests

    [Test]
    public void UsuarioSalio_UsuarioLuis_TextoEsperado()
    {
        // Act
        var mensaje = MensajesSistema.UsuarioSalio("Luis");

        // Assert
        mensaje.Texto.Should().Be("Luis ha salido del canal");
    }

    [Test]
    public void UsuarioSalio_TipoSistema()
    {
        // Act
        var mensaje = MensajesSistema.UsuarioSalio("Luis");

        // Assert
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);
    }

    #endregion

    #region NickCambiado Tests

    [Test]
    public void NickCambiado_NombresDados_TextoEsperado()
    {
        // Act
        var mensaje = MensajesSistema.NickCambiado("Ana", "Ana2");

        // Assert
        mensaje.Texto.Should().Be("Ana ahora se llama Ana2");
    }

    [Test]
    public void NickCambiado_TipoSistema_YAutorSistema()
    {
        // Act
        var mensaje = MensajesSistema.NickCambiado("Ana", "Ana2");

        // Assert
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);
        mensaje.Usuario.Should().Be(MensajesSistema.Autor);
    }

    #endregion

    #region Tests comunes

    [Test]
    public void Crear_DosLlamadas_IdentificadoresDistintos()
    {
        // Act
        var mensaje1 = MensajesSistema.UsuarioUnido("Ana");
        var mensaje2 = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje1.Id.Should().NotBe(mensaje2.Id);
    }

    [Test]
    public void Crear_TimestampReciente()
    {
        // Arrange
        var antes = DateTime.UtcNow;

        // Act
        var mensaje = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje.Timestamp.Should().BeOnOrAfter(antes).And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion
}
