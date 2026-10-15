using AulaChatNet.Client.Terminal.Consola;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;

namespace AulaChatNet.Client.Terminal.Tests.Consola;

/// <summary>
/// Suite de tests para Conectados: las fórmulas de "cuánta gente hay"
/// (mensaje de arranque y línea de estado). Son cadenas puras, así que se
/// comprueban sin red, sin servidor y sin consola.
/// </summary>
[TestFixture]
public class ConectadosTests
{
    private ChatConfig _chat = null!;

    [SetUp]
    public void Setup()
    {
        // Mismo orden que appsettings: general, daw, dam, asir, smr
        _chat = new ChatConfig
        {
            Canales = ["#general", "#daw", "#dam", "#asir", "#smr"]
        };
    }

    [Test]
    public void DescripcionArranque_CeroConectados_DiceQueNoHayNadie()
    {
        // Arrange: el servidor no emite salas vacías
        var est = new EstadisticasDto(0, []);

        // Act
        var texto = Conectados.DescripcionArranque(_chat, est);

        // Assert
        texto.Should().Contain("no hay nadie conectado");
    }

    [Test]
    public void DescripcionArranque_UnaConectada_UsaSingular()
    {
        // Arrange
        var est = new EstadisticasDto(1, [new ConteoCanalDto("#general", 1)]);

        // Act
        var texto = Conectados.DescripcionArranque(_chat, est);

        // Assert: "1 persona conectada", nunca "1 personas"
        texto.Should().Contain("1 persona conectada")
            .And.NotContain("personas conectadas");
    }

    [Test]
    public void DescripcionArranque_Varias_UsaPluralYElTotal()
    {
        // Arrange
        var est = new EstadisticasDto(20,
        [
            new ConteoCanalDto("#general", 12),
            new ConteoCanalDto("#daw", 8)
        ]);

        // Act
        var texto = Conectados.DescripcionArranque(_chat, est);

        // Assert
        texto.Should().Contain("20 personas conectadas");
    }

    [Test]
    public void DescripcionArranque_LlevaElRepartoPorSala()
    {
        // Arrange
        var est = new EstadisticasDto(3,
        [
            new ConteoCanalDto("#general", 2),
            new ConteoCanalDto("#daw", 1)
        ]);

        // Act
        var texto = Conectados.DescripcionArranque(_chat, est);

        // Assert: total Y desglose en la misma línea
        texto.Should().Contain("3 personas conectadas")
            .And.Contain("#general");
    }

    [Test]
    public void ConteosPorSala_RespetaElOrdenDeLaConfiguracion()
    {
        // Arrange: el servidor manda las salas por POBLACIÓN (daw primero),
        // pero en pantalla manda appsettings (general, daw, dam, asir, smr)
        var est = new EstadisticasDto(3,
        [
            new ConteoCanalDto("#daw", 2),
            new ConteoCanalDto("#general", 1)
        ]);

        // Act
        var texto = Conectados.ConteosPorSala(_chat.Canales, est, canalActual: null);

        // Assert
        texto.IndexOf("#general", StringComparison.Ordinal)
            .Should().BeLessThan(texto.IndexOf("#daw", StringComparison.Ordinal));
        texto.IndexOf("#daw", StringComparison.Ordinal)
            .Should().BeLessThan(texto.IndexOf("#dam", StringComparison.Ordinal));
        texto.IndexOf("#dam", StringComparison.Ordinal)
            .Should().BeLessThan(texto.IndexOf("#asir", StringComparison.Ordinal));
        texto.IndexOf("#asir", StringComparison.Ordinal)
            .Should().BeLessThan(texto.IndexOf("#smr", StringComparison.Ordinal));
    }

    [Test]
    public void ConteosPorSala_SalaSinGente_PintaCero()
    {
        // Arrange: solo #general tiene gente; el resto el servidor no lo emite
        var est = new EstadisticasDto(1, [new ConteoCanalDto("#general", 1)]);

        // Act
        var texto = Conectados.ConteosPorSala(_chat.Canales, est, canalActual: null);

        // Assert: la terminal pinta el 0 ella misma
        texto.Should().Contain("#smr [white]0[/]");
    }

    [Test]
    public void ConteosPorSala_CanalActual_LoMarcaConPuntoVerde()
    {
        // Arrange
        var est = new EstadisticasDto(2,
        [
            new ConteoCanalDto("#general", 1),
            new ConteoCanalDto("#daw", 1)
        ]);

        // Act
        var texto = Conectados.ConteosPorSala(_chat.Canales, est, canalActual: "#daw");

        // Assert
        texto.Should().Contain("[green]#daw 1 ●[/]");
    }
}
