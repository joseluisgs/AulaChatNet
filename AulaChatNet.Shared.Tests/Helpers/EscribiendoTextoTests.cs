using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Pruebas de la frase del indicador: quién está escribiendo, en plano y con
/// la conjugación correcta. El markup de cada cliente se prueba en SU suite.
/// </summary>
[TestFixture]
public class EscribiendoTextoTests
{
    [Test]
    public void Descripcion_SinNadie_DevuelveCadenaVacia()
    {
        // Act
        var frase = EscribiendoTexto.Descripcion([]);

        // Assert: quien la recibe no pinta nada con una cadena vacía
        frase.Should().BeEmpty();
    }

    [Test]
    public void Descripcion_UnaPersona_DiceQueEstaEscribiendo()
    {
        // Act
        var frase = EscribiendoTexto.Descripcion(["Ana"]);

        // Assert: con una sola persona, singular
        frase.Should().Be("Ana está escribiendo...");
    }

    [Test]
    public void Descripcion_DosPersonas_DiceQueEstanEscribiendo()
    {
        // Act
        var frase = EscribiendoTexto.Descripcion(["Ana", "Bruno"]);

        // Assert: con dos o más, plural y separadas por coma
        frase.Should().Be("Ana, Bruno están escribiendo...");
    }
}
