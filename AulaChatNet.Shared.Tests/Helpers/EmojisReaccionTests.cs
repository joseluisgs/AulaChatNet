using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Suite de tests para EmojisReaccion: la lista cerrada que comparten
/// servidor, terminal y web. Se prueba con esmero porque TODA la defensa de
/// "no ensuciar el canal" pende de aquí: si la lista se abre por accidente,
/// se abre para los tres clientes a la vez.
/// </summary>
[TestFixture]
public class EmojisReaccionTests
{
    #region Lista Tests

    [Test]
    public void Admitidos_TraeLosSeisEmojiSinDuplicados()
    {
        // Assert: seis, y ninguno repetido — un duplicado sería el mismo
        // chip pintado dos veces con la misma cara
        EmojisReaccion.Admitidos.Should().HaveCount(6);
        EmojisReaccion.Admitidos.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void ListaLegible_EnseñaTodosLosEmojiSeparadosPorComa()
    {
        // Assert: es el texto que verá quien se equivoque al teclear
        EmojisReaccion.ListaLegible.Should().Contain("👍");
        EmojisReaccion.ListaLegible.Should().Contain("😂");
        EmojisReaccion.ListaLegible.Should().Contain(", ");
    }

    #endregion

    #region Canonico Tests

    [Test]
    [TestCase("👍")]
    [TestCase("❤️")]
    [TestCase("😂")]
    [TestCase("😮")]
    [TestCase("😢")]
    [TestCase("😡")]
    public void Canonico_EmojiAdmitido_DevuelveElMismo(string emoji)
    {
        // Act
        var canonico = EmojisReaccion.Canonico(emoji);

        // Assert
        canonico.Should().Be(emoji);
    }

    [Test]
    public void Canonico_ConEspaciosAlrededor_LosRecorta()
    {
        // Act: el parser ya recorta, pero la red es de todos
        var canonico = EmojisReaccion.Canonico("  👍  ");

        // Assert
        canonico.Should().Be("👍");
    }

    [Test]
    public void Canonico_CorazonSinSelector_DevuelveElCanonicoConSelector()
    {
        // Act: ❤ (sin selector de presentación) y ❤️ son el MISMO gráfico
        // en dos cadenas distintas; el chip pintado debe ser siempre el mismo
        var canonico = EmojisReaccion.Canonico("\u2764");

        // Assert
        canonico.Should().Be("❤️");
    }

    [Test]
    [TestCase("hola")]
    [TestCase("👍👍")]          // dos emoji pegados: no es NINGUNO de la lista
    [TestCase("🔥")]            // emoji bonito, pero fuera de la lista
    [TestCase("7")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Canonico_EmojiNoAdmitido_DevuelveNull(string? entrada)
    {
        // Act
        var canonico = EmojisReaccion.Canonico(entrada);

        // Assert: null = "eso no es un emoji de la lista"; quien llama avisa
        canonico.Should().BeNull();
    }

    #endregion
}
