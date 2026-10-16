using AulaChatNet.Client.Terminal.Servicios;
using FluentAssertions;
using System.Net.Sockets;
using System.Text.Json;

namespace AulaChatNet.Client.Terminal.Tests.Servicios;

/// <summary>
/// Suite de tests para MensajesError: traducir excepciones a mensajes que
/// entienda quien usa el chat. La regla que se verifica en cada caso: en
/// pantalla NUNCA aparece el tipo de la excepción ni su mensaje técnico.
/// </summary>
[TestFixture]
public class MensajesErrorTests
{
    #region Traducción a mensajes de usuario Tests

    [Test]
    public void DescripcionDe_HttpRequestException_ExplicaQueNoRespondeSinNombrarla()
    {
        // Act
        var mensaje = MensajesError.DescripcionDe(new HttpRequestException("connect err"));

        // Assert
        mensaje.Should().Contain("no responde")
            .And.NotContain("HttpRequestException")
            .And.NotContain("Exception");
    }

    [Test]
    public void DescripcionDe_TiempoDeEsperaAgotado_AvisaQueTardoMucho()
    {
        // Act: los timeouts llegan como TaskCanceledException
        var mensaje = MensajesError.DescripcionDe(new TaskCanceledException());

        // Assert
        mensaje.Should().Contain("tardó demasiado")
            .And.NotContain("Canceled");
    }

    [Test]
    public void DescripcionDe_SocketException_HablaDeRedYNoDeSockets()
    {
        // Act (10061 = conexión rechazada; el código no importa para el mensaje)
        var mensaje = MensajesError.DescripcionDe(new SocketException(10061));

        // Assert
        mensaje.Should().Contain("red")
            .And.NotContain("Socket");
    }

    [Test]
    public void DescripcionDe_Json_InformaQueLaRespuestaNoEraLaEsperada()
    {
        // Act
        var mensaje = MensajesError.DescripcionDe(new JsonException("<<no json>>"));

        // Assert
        mensaje.Should().Contain("datos que no se esperaban")
            .And.NotContain("JsonException");
    }

    [Test]
    public void DescripcionDe_ExcepcionDesconocida_NoFiltraNiElTipoNiElMensajeTecnico()
    {
        // Act: cualquier otra sorpresa
        var mensaje = MensajesError.DescripcionDe(
            new InvalidOperationException("detalle interno secreto"));

        // Assert: mensaje honesto, sin asustar ni filtrar detalles
        mensaje.Should().Contain("inesperado")
            .And.NotContain("InvalidOperationException")
            .And.NotContain("detalle interno secreto");
    }

    #endregion
}
