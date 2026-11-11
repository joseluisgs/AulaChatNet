using AulaChatNet.Client.Terminal.Consola;
using FluentAssertions;
using Spectre.Console;

namespace AulaChatNet.Client.Terminal.Tests.Consola;

/// <summary>
///     Suite de tests para TemaDiscord: los colores e iniciales derivados del
///     nombre. Es lógica pura (hash determinista), así que se prueba sin UI.
/// </summary>
[TestFixture]
public class TemaDiscordTests {
    [Test]
    [TestCase("Ana")]
    [TestCase("Luis Miguel")]
    [TestCase("")]
    public void ColorDe_MismoNombre_SiempreDevuelveElMismoColor(string usuario) {
        // Act: dos llamadas independientes
        Color primero = TemaDiscord.ColorDe(usuario);
        Color segundo = TemaDiscord.ColorDe(usuario);

        // Assert: el color es una función del nombre, no hay estado
        segundo.Should().Be(primero);
    }

    [Test]
    public void ColorDe_NombreNormal_NoDevuelveColorDeFondoNiBlanco() {
        // Act: el color debe ser legible sobre el fondo oscuro de Discord
        Color color = TemaDiscord.ColorDe("Ana");

        // Assert: la paleta no incluye ni el fondo ni el blanco puro
        color.Should().NotBe(TemaDiscord.Fondo).And.NotBe(TemaDiscord.Texto);
    }

    [Test]
    [TestCase("Ana", "AN")]
    [TestCase("carlos", "CA")]
    [TestCase("José Luis", "JL")]
    [TestCase("luis miguel", "LM")]
    [TestCase("  maria   garcia ", "MG")] // espacios sobrantes
    public void Iniciales_NombreDado_DevuelveDosLetras(string usuario, string esperado) {
        // Act
        string iniciales = TemaDiscord.Iniciales(usuario);

        // Assert
        iniciales.Should().Be(esperado);
    }

    [Test]
    public void Iniciales_NombreDeUnaLetra_DevuelveEsaLetra() {
        // Act: caso límite, un solo carácter
        string iniciales = TemaDiscord.Iniciales("A");

        // Assert
        iniciales.Should().Be("A");
    }

    [Test]
    public void Iniciales_NombreVacio_DevuelveMarcadorDeDesconocido() {
        // Act
        string iniciales = TemaDiscord.Iniciales("");

        // Assert: nunca cadena vacía (rompería el markup de la consola)
        iniciales.Should().Be("?");
    }
}
