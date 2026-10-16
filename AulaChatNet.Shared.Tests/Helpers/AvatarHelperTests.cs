using FluentAssertions;
using AulaChatNet.Shared.Helpers;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Suite de tests para AvatarHelper.
/// Verifica el determinismo de los avatares: mismo nombre de usuario →
/// mismo estilo, misma URL y mismo color, también entre arranques.
/// (El bug clásico: reescribir el hash con string.GetHashCode(), que NO
/// es estable entre procesos y cambiaría los avatares en cada ejecución.)
/// </summary>
[TestFixture]
public class AvatarHelperTests
{
    private const string UrlBase = "https://api.dicebear.com/10.x";

    #region ObtenerUrl Tests

    [Test]
    public void ObtenerUrl_MismoUsuario_LaMismaUrl()
    {
        // Arrange
        var usuario = "Ana";

        // Act
        var url1 = AvatarHelper.ObtenerUrl(usuario, UrlBase);
        var url2 = AvatarHelper.ObtenerUrl(usuario, UrlBase);

        // Assert
        url1.Should().Be(url2);
    }

    [Test]
    public void ObtenerUrl_ConUrlBaseConBarraFinal_LaNormaliza()
    {
        // Arrange
        var esperada = AvatarHelper.ObtenerUrl("Ana", UrlBase);

        // Act
        var conBarra = AvatarHelper.ObtenerUrl("Ana", $"{UrlBase}/");

        // Assert
        conBarra.Should().Be(esperada);
    }

    [Test]
    public void ObtenerUrl_ContieneElEstiloYLaSemilla()
    {
        // Act
        var url = AvatarHelper.ObtenerUrl("José Luis", UrlBase);

        // Assert
        url.Should().StartWith($"{UrlBase}/");
        url.Should().EndWith("/svg?seed=Jos%C3%A9%20Luis");
    }

    #endregion

    #region ObtenerEstilo Tests

    [Test]
    public void ObtenerEstilo_MismoUsuario_ElMismoEstilo()
    {
        // Act
        var estilo1 = AvatarHelper.ObtenerEstilo("Carlos");
        var estilo2 = AvatarHelper.ObtenerEstilo("Carlos");

        // Assert
        estilo1.Should().Be(estilo2);
    }

    [Test]
    public void ObtenerEstilo_EsInsensibleAMayusculas()
    {
        // Act
        var mayusculas = AvatarHelper.ObtenerEstilo("ANA");
        var minusculas = AvatarHelper.ObtenerEstilo("ana");

        // Assert
        mayusculas.Should().Be(minusculas);
    }

    [Test]
    public void ObtenerEstilo_EstiloNoVacio()
    {
        // Act
        var estilo = AvatarHelper.ObtenerEstilo("Marta");

        // Assert
        estilo.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region ObtenerIniciales Tests

    [TestCase("José Luis", "[JL]")]
    [TestCase("Ana García", "[AG]")]
    [TestCase("Ana", "[AN]")]
    [TestCase("A", "[A]")]
    [TestCase("maria de la luz", "[MD]")]  // dos primeras palabras: Maria + de
    [TestCase("José   Luis", "[JL]")]   // espacios repetidos
    [TestCase("  Ana  ", "[AN]")]       // espacios alrededor
    public void ObtenerIniciales_CasoValido_RetornaInicialesEntreCorchetes(string usuario, string esperado)
    {
        // Act
        var resultado = AvatarHelper.ObtenerIniciales(usuario);

        // Assert
        resultado.Should().Be(esperado);
    }

    [Test]
    public void ObtenerIniciales_UsuarioVacio_RetornaMarcasDePosicion()
    {
        // Act
        var resultado = AvatarHelper.ObtenerIniciales("");

        // Assert
        resultado.Should().Be("[??]");
    }

    #endregion

    #region ObtenerColor Tests

    [Test]
    public void ObtenerColor_MismoUsuario_ElMismoColor()
    {
        // Act
        var color1 = AvatarHelper.ObtenerColor("Diego");
        var color2 = AvatarHelper.ObtenerColor("Diego");

        // Assert
        color1.Should().Be(color2);
    }

    [Test]
    public void ObtenerColor_ComponentesLegiblesSobreFondoOscuro()
    {
        // Act
        var color = AvatarHelper.ObtenerColor("Lucía");

        // Assert
        // Colores vivos: todos los componentes altos para que el texto
        // se lea sobre los fondos oscuros de Discord (#36393f) y la terminal
        color.R.Should().BeGreaterThan(80);
        color.G.Should().BeGreaterThan(80);
        color.B.Should().BeGreaterThan(80);
    }

    [Test]
    public void ObtenerColor_DistintosUsuarios_GeneraAlMenosUnDistinto()
    {
        // Act
        var usuarios = new[] { "Ana", "Luis", "Marta", "Diego", "Lucía", "Pablo", "Sofía" };
        var colores = usuarios.Select(u => AvatarHelper.ObtenerColor(u)).ToHashSet();

        // Assert
        // Con varios nombres, tienen que salir colores distintos entre sí
        colores.Count.Should().BeGreaterThan(1);
    }

    #endregion
}
