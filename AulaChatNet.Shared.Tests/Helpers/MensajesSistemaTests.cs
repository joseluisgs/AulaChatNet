using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
///     Suite de tests para MensajesSistema.
///     Son los mensajes por los que los clientes se enteran de las entradas
///     Y las desconexiones ("X ha salido del canal"), viajando por el mismo
///     flujo que los mensajes normales.
/// </summary>
[TestFixture]
public class MensajesSistemaTests {
    [Test]
    public void UsuarioUnido_UsuarioAna_TextoEsperado() {
        // Act
        MensajeDto mensaje = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje.Texto.Should().Be("Ana se ha unido al canal");
    }

    [Test]
    public void UsuarioUnido_TipoSistema_YAutorSistema() {
        // Act
        MensajeDto mensaje = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);
        mensaje.Usuario.Should().Be(MensajesSistema.Autor);
    }

    [Test]
    public void UsuarioSalio_UsuarioLuis_TextoEsperado() {
        // Act
        MensajeDto mensaje = MensajesSistema.UsuarioSalio("Luis");

        // Assert
        mensaje.Texto.Should().Be("Luis ha salido del canal");
    }

    [Test]
    public void UsuarioSalio_TipoSistema() {
        // Act
        MensajeDto mensaje = MensajesSistema.UsuarioSalio("Luis");

        // Assert
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);
    }

    [Test]
    public void NickCambiado_NombresDados_TextoEsperado() {
        // Act
        MensajeDto mensaje = MensajesSistema.NickCambiado("Ana", "Ana2");

        // Assert
        mensaje.Texto.Should().Be("Ana ahora se llama Ana2");
    }

    [Test]
    public void NickCambiado_TipoSistema_YAutorSistema() {
        // Act
        MensajeDto mensaje = MensajesSistema.NickCambiado("Ana", "Ana2");

        // Assert
        mensaje.Tipo.Should().Be(TipoMensaje.Sistema);
        mensaje.Usuario.Should().Be(MensajesSistema.Autor);
    }

    [Test]
    public void Crear_DosLlamadas_IdentificadoresDistintos() {
        // Act
        MensajeDto mensaje1 = MensajesSistema.UsuarioUnido("Ana");
        MensajeDto mensaje2 = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje1.Id.Should().NotBe(mensaje2.Id);
    }

    [Test]
    public void Crear_TimestampReciente() {
        // Arrange
        DateTime antes = DateTime.UtcNow;

        // Act
        MensajeDto mensaje = MensajesSistema.UsuarioUnido("Ana");

        // Assert
        mensaje.Timestamp.Should().BeOnOrAfter(antes).And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
