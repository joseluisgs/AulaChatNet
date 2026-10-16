using AulaChatNet.Client.Terminal.Servicios;
using AulaChatNet.Shared.Configuracion;
using FluentAssertions;

namespace AulaChatNet.Client.Terminal.Tests.Servicios;

/// <summary>
/// Suite de tests para ClienteChat: los caminos que NO necesitan servidor.
/// El diferencial nuevo del contrato es saber SI el servidor respondió
/// (HayServidor): de eso depende que el arranque repregunte el nick o
/// conserve el que ya se escribió.
/// </summary>
[TestFixture]
public class ClienteChatTests
{
    #region Conexión sin servidor Tests

    [Test]
    public async Task ConectarAsync_SinServidor_DevuelveFalloDeRedSinTiposDeExcepcion()
    {
        // Arrange: nadie escucha en ese puerto → la conexión se rechaza al instante
        var chat = new ChatConfig { Canales = ["#general"] };
        using var cliente = new ClienteChat("http://127.0.0.1:59999/hubs/chat", chat);

        // Act
        var resultado = await cliente.ConectarAsync("Ana", "#general");

        // Assert: QUÉ falló viaja tipado (no hay servidor → no se repregunta el nick)
        resultado.Exitoso.Should().BeFalse();
        resultado.HayServidor.Should().BeFalse();

        // Y el mensaje es para el usuario: ni tipos ni mensajes técnicos
        resultado.Error.Should()
            .Contain("No se pudo conectar con el servidor")
            .And.NotContain("Exception");

        cliente.EstadoValor.Should().Be(EstadoConexion.Desconectado);
    }

    #endregion
}
