using AulaChatNet.Client.Terminal.Servicios;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
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

    #region Conexión con rechazo del servidor Tests

    [Test]
    public async Task ConectarAsync_ServidorRechazaElNick_DevuelveRechazoConServidor()
    {
        // Arrange: servidor de prueba mínimo que siempre rechaza el alta
        await using var servidor = await ServidorDePrueba.CrearAsync();
        var chat = new ChatConfig { Canales = ["#general"] };
        using var cliente = new ClienteChat($"{servidor.Url}/hubs/chat", chat);

        // Act
        var resultado = await cliente.ConectarAsync("Ana", "#general");

        // Assert: el servidor RESPONDIÓ → HayServidor = true (aquí sí toca
        // repreguntar el nick, que es justo lo contrario que en el test anterior)
        resultado.Exitoso.Should().BeFalse();
        resultado.HayServidor.Should().BeTrue();

        // El texto del servidor se traslada tal cual (eso sí, ya sin excepciones)
        resultado.Error.Should().Be("Ya hay un Ana en #general");

        cliente.EstadoValor.Should().Be(EstadoConexion.Desconectado);
    }

    #endregion
}
