using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Suite de tests para CombinadorChat: mezcla canal y privados en una sola
/// cronología. Es la lógica que comparten la terminal y la web, así que se
/// prueba UNA vez aquí y vale para los dos clientes.
/// </summary>
[TestFixture]
public class CombinadorChatTests
{
    /// <summary>Crea un mensaje del canal con la hora dada.</summary>
    private static MensajeDto Mensaje(string texto, int hora) =>
        new(Guid.NewGuid(), "Ana", texto, new DateTime(2026, 3, 2, hora, 0, 0, DateTimeKind.Utc), TipoMensaje.Normal);

    /// <summary>Crea un mensaje privado con la hora dada.</summary>
    private static MensajePrivadoDto Privado(string texto, int hora) =>
        new(Guid.NewGuid(), "Ana", "Luis", texto, new DateTime(2026, 3, 2, hora, 0, 0, DateTimeKind.Utc));

    [Test]
    public void Combinar_SinPrivados_DevuelveElCanalSinMas()
    {
        // Arrange
        IReadOnlyList<MensajeDto> canal = [Mensaje("Hola", 10), Mensaje("Buenas", 11)];

        // Act
        var filas = CombinadorChat.Combinar(canal, []);

        // Assert: mismo número, en el mismo orden, y ninguna marcada como privada
        filas.Should().HaveCount(2);
        filas.Should().OnlyContain(f => !f.EsPrivado);
        filas[0].Mensaje!.Texto.Should().Be("Hola");
    }

    [Test]
    public void Combinar_SinCanal_DevuelveSoloLosPrivados()
    {
        // Arrange
        IReadOnlyList<MensajePrivadoDto> privados = [Privado("Confidencia", 12)];

        // Act
        var filas = CombinadorChat.Combinar([], privados);

        // Assert
        filas.Should().ContainSingle().Which.EsPrivado.Should().BeTrue();
    }

    [Test]
    public void Combinar_FechasEntremezcladas_LasOrdenaPorFecha()
    {
        // Arrange: canal con 10:00 y 12:00; privados con 11:00 y 13:00.
        // Concatenarlos tal cual dejaría los privados por DEBAJO de los
        // mensajes posteriores, que es el error que previene este test.
        IReadOnlyList<MensajeDto> canal = [Mensaje("Mensaje de las 10", 10), Mensaje("Mensaje de las 12", 12)];
        IReadOnlyList<MensajePrivadoDto> privados = [Privado("Privado de las 11", 11), Privado("Privado de las 13", 13)];

        // Act
        var filas = CombinadorChat.Combinar(canal, privados);

        // Assert
        filas.Should().HaveCount(4);
        filas.Select(f => f.EsPrivado).Should().Equal(false, true, false, true);
    }

    [Test]
    public void Combinar_MismoMomento_MantieneElCanalPorDelante()
    {
        // Arrange: el canal llega REEMPLAZADO y los privados a su ritmo, así
        // que dos cosas con la misma hora pueden cruzarse; el orden estable
        // de LINQ evita que se den la vuelta sin motivo
        IReadOnlyList<MensajeDto> canal = [Mensaje("Del canal", 10)];
        IReadOnlyList<MensajePrivadoDto> privados = [Privado("Del privado", 10)];

        // Act
        var filas = CombinadorChat.Combinar(canal, privados);

        // Assert
        filas.Should().HaveCount(2);
        filas[0].EsPrivado.Should().BeFalse();
        filas[1].EsPrivado.Should().BeTrue();
    }

    [Test]
    public void Combinar_NoPierdeNadaDeNingunaLista()
    {
        // Arrange
        IReadOnlyList<MensajeDto> canal = [Mensaje("Uno", 9), Mensaje("Dos", 10)];
        IReadOnlyList<MensajePrivadoDto> privados = [Privado("Tres", 9), Privado("Cuatro", 11)];

        // Act
        var filas = CombinadorChat.Combinar(canal, privados);

        // Assert: todo lo que entra, sale
        filas.Should().HaveCount(4);
        filas.Count(f => f.EsPrivado).Should().Be(2);
        filas.Count(f => !f.EsPrivado).Should().Be(2);
    }

    [Test]
    [TestCase(null, null)]
    public void Combinar_ListasNulas_LanzaExcepcion(IReadOnlyList<MensajeDto>? canal, IReadOnlyList<MensajePrivadoDto>? privados)
    {
        // Act
        var accion = () => CombinadorChat.Combinar(canal!, privados!);

        // Assert: fallar rápido y en desarrollo, no en medio de un repintado
        accion.Should().Throw<ArgumentNullException>();
    }
}
