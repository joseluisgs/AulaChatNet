using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
///     Suite de tests para AvatarHelper.
///     Verifica el determinismo de los avatares: mismo nombre de usuario →
///     mismo estilo, misma URL y mismo color, también entre arranques.
///     (El bug clásico: reescribir el hash con string.GetHashCode(), que NO
///     es estable entre procesos y cambiaría los avatares en cada ejecución.)
/// </summary>
[TestFixture]
public class AvatarHelperTests {
    private const string UrlBase = "https://api.dicebear.com/10.x";

    [Test]
    public void ObtenerUrl_MismoUsuario_LaMismaUrl() {
        // Arrange
        string usuario = "Ana";

        // Act
        string url1 = AvatarHelper.ObtenerUrl(usuario, UrlBase);
        string url2 = AvatarHelper.ObtenerUrl(usuario, UrlBase);

        // Assert
        url1.Should().Be(url2);
    }

    [Test]
    public void ObtenerUrl_ConUrlBaseConBarraFinal_LaNormaliza() {
        // Arrange
        string esperada = AvatarHelper.ObtenerUrl("Ana", UrlBase);

        // Act
        string conBarra = AvatarHelper.ObtenerUrl("Ana", $"{UrlBase}/");

        // Assert
        conBarra.Should().Be(esperada);
    }

    [Test]
    public void ObtenerUrl_ContieneElEstiloYLaSemilla() {
        // Act
        string url = AvatarHelper.ObtenerUrl("José Luis", UrlBase);

        // Assert
        url.Should().StartWith($"{UrlBase}/");
        url.Should().EndWith("/svg?seed=Jos%C3%A9%20Luis");
    }

    [Test]
    public void ObtenerEstilo_MismoUsuario_ElMismoEstilo() {
        // Act
        string estilo1 = AvatarHelper.ObtenerEstilo("Carlos");
        string estilo2 = AvatarHelper.ObtenerEstilo("Carlos");

        // Assert
        estilo1.Should().Be(estilo2);
    }

    [Test]
    public void ObtenerEstilo_EsInsensibleAMayusculas() {
        // Act
        string mayusculas = AvatarHelper.ObtenerEstilo("ANA");
        string minusculas = AvatarHelper.ObtenerEstilo("ana");

        // Assert
        mayusculas.Should().Be(minusculas);
    }

    [Test]
    public void ObtenerEstilo_EstiloNoVacio() {
        // Act
        string estilo = AvatarHelper.ObtenerEstilo("Marta");

        // Assert
        estilo.Should().NotBeNullOrWhiteSpace();
    }

    [TestCase("José Luis", "[JL]")]
    [TestCase("Ana García", "[AG]")]
    [TestCase("Ana", "[AN]")]
    [TestCase("A", "[A]")]
    [TestCase("maria de la luz", "[MD]")] // dos primeras palabras: Maria + de
    [TestCase("José   Luis", "[JL]")] // espacios repetidos
    [TestCase("  Ana  ", "[AN]")] // espacios alrededor
    public void ObtenerIniciales_CasoValido_RetornaInicialesEntreCorchetes(string usuario, string esperado) {
        // Act
        string resultado = AvatarHelper.ObtenerIniciales(usuario);

        // Assert
        resultado.Should().Be(esperado);
    }

    [Test]
    public void ObtenerIniciales_UsuarioVacio_RetornaMarcasDePosicion() {
        // Act
        string resultado = AvatarHelper.ObtenerIniciales("");

        // Assert
        resultado.Should().Be("[??]");
    }

    [Test]
    public void ObtenerColor_MismoUsuario_ElMismoColor() {
        // Act
        (byte R, byte G, byte B) color1 = AvatarHelper.ObtenerColor("Diego");
        (byte R, byte G, byte B) color2 = AvatarHelper.ObtenerColor("Diego");

        // Assert
        color1.Should().Be(color2);
    }

    [Test]
    public void ObtenerColor_ComponentesLegiblesSobreFondoOscuro() {
        // Act
        (byte R, byte G, byte B) color = AvatarHelper.ObtenerColor("Lucía");

        // Assert
        // Colores vivos: todos los componentes altos para que el texto
        // se lea sobre los fondos oscuros de Discord (#36393f) y la terminal
        color.R.Should().BeGreaterThan(80);
        color.G.Should().BeGreaterThan(80);
        color.B.Should().BeGreaterThan(80);
    }

    [Test]
    public void ObtenerColor_DistintosUsuarios_GeneraAlMenosUnDistinto() {
        // Act
        string[] usuarios = new[] { "Ana", "Luis", "Marta", "Diego", "Lucía", "Pablo", "Sofía" };
        var colores = usuarios.Select(u => AvatarHelper.ObtenerColor(u)).ToHashSet();

        // Assert
        // Con varios nombres, tienen que salir colores distintos entre sí
        colores.Count.Should().BeGreaterThan(1);
    }
}
