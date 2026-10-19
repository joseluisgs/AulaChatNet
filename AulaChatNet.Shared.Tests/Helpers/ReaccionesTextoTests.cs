using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Suite de tests para ReaccionesTexto: la frase PLANA que describen los
/// chips ("👍 2 (Ana, Bruno)"). El markup de cada cliente se prueba en SU
/// suite; aquí solo manda el texto.
/// </summary>
[TestFixture]
public class ReaccionesTextoTests
{
    [Test]
    [TestCase(null)]
    public void Descripcion_SinReacciones_DevuelveCadenaVacia(IReadOnlyList<ReaccionDto>? entrada)
    {
        // Act: quien la recibe no pinta nada con una cadena vacía
        var frase = ReaccionesTexto.Descripcion(entrada);

        // Assert
        frase.Should().BeEmpty();
    }

    [Test]
    public void Descripcion_ListaVacia_DevuelveCadenaVacia()
    {
        // Act: así llega el DTO cuando alguien quitó la última reacción
        var frase = ReaccionesTexto.Descripcion(new List<ReaccionDto>());

        // Assert: cadena vacía = "borra la fila de reacciones"
        frase.Should().BeEmpty();
    }

    [Test]
    public void Descripcion_UnaPersona_DiceEmojiContadorYNombre()
    {
        // Act
        var frase = ReaccionesTexto.Descripcion(
            [new ReaccionDto("👍", ["Ana"])]);

        // Assert
        frase.Should().Be("👍 1 (Ana)");
    }

    [Test]
    public void Descripcion_DosChips_LosUneConEspacio()
    {
        // Act: el orden lo decide quien envía la lista (el de la lista cerrada)
        var frase = ReaccionesTexto.Descripcion(
        [
            new ReaccionDto("👍", ["Ana", "Bruno"]),
            new ReaccionDto("😂", ["Luis"])
        ]);

        // Assert
        frase.Should().Be("👍 2 (Ana, Bruno) 😂 1 (Luis)");
    }

    [Test]
    public void Descripcion_MasDeTresNombres_LosCortaConPuntos()
    {
        // Act: un emoji muy popular no debe convertir la fila en un muro
        var frase = ReaccionesTexto.Descripcion(
            [new ReaccionDto("👍", ["Ana", "Bruno", "Clara", "Diego"])]);

        // Assert: se listan tres y se corta, pero el CONTADOR es el real
        frase.Should().Be("👍 4 (Ana, Bruno, Clara...)");
    }

    [Test]
    public void Descripcion_ChipSinPersonas_PintaElContadorEnCero()
    {
        // Act: caso defensivo; el servidor nunca emite un chip vacío
        var frase = ReaccionesTexto.Descripcion(
            [new ReaccionDto("👍", [])]);

        // Assert
        frase.Should().Be("👍 0");
    }
}
