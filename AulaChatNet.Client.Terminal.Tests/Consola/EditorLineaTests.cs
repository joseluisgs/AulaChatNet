using AulaChatNet.Client.Terminal.Consola;
using FluentAssertions;

namespace AulaChatNet.Client.Terminal.Tests.Consola;

/// <summary>
/// Suite de tests para EditorLinea: la lógica del cursor de la línea de
/// entrada de la vista en vivo. Al ser una clase pura (sin consola ni
/// teclado), todos estos tests corren en milisegundos sin ventana alguna.
/// </summary>
[TestFixture]
public class EditorLineaTests
{
    private EditorLinea _editor = null!;

    [SetUp]
    public void Setup()
    {
        _editor = new EditorLinea();
    }

    #region Insertar

    [Test]
    public void Insertar_AlFinal_DejaElTextoYElCursorAlFinal()
    {
        // Act
        _editor.Insertar('a');
        _editor.Insertar('n');
        _editor.Insertar('a');

        // Assert
        _editor.Texto.Should().Be("ana");
        _editor.Posicion.Should().Be(3);
        _editor.Longitud.Should().Be(3);
    }

    [Test]
    public void Insertar_EnMedio_InsertaEnLaPosicionDelCursor()
    {
        // Arrange: "ao" con el cursor entre la 'a' y la 'o'
        _editor.Insertar('a');
        _editor.Insertar('o');
        _editor.MoverIzquierda();

        // Act
        _editor.Insertar('n');

        // Assert: salió "ano" y el cursor siguió al carácter nuevo
        _editor.Texto.Should().Be("ano");
        _editor.Posicion.Should().Be(2);
    }

    #endregion

    #region Borrar

    [Test]
    public void BorrarAtras_BorraElCaracterAnteriorAlCursor()
    {
        // Arrange
        _editor.Insertar('a');
        _editor.Insertar('n');
        _editor.Insertar('a');
        _editor.MoverIzquierda(); // cursor sobre la 'a' final

        // Act
        _editor.BorrarAtras();

        // Assert: retrocede borrando la 'n'
        _editor.Texto.Should().Be("aa");
        _editor.Posicion.Should().Be(1);
    }

    [Test]
    public void BorrarAtras_EnElInicio_NoTocaElTexto()
    {
        // Arrange: texto con el cursor ya AL PRINCIPIO
        _editor.Insertar('a');
        _editor.Inicio();

        // Act: no hay carácter anterior
        _editor.BorrarAtras();
        _editor.BorrarAtras();

        // Assert: nada que borrar al principio, pero NO revienta
        _editor.Texto.Should().Be("a");
        _editor.Posicion.Should().Be(0);
    }

    [Test]
    public void BorrarDelante_BorraElCaracterBajoElCursor()
    {
        // Arrange: "ana" con el cursor al principio
        _editor.Insertar('a');
        _editor.Insertar('n');
        _editor.Insertar('a');
        _editor.Inicio();

        // Act
        _editor.BorrarDelante();

        // Assert: se va la 'a' de delante y el cursor se queda donde estaba
        _editor.Texto.Should().Be("na");
        _editor.Posicion.Should().Be(0);
    }

    [Test]
    public void BorrarDelante_AlFinal_NoTocaElTexto()
    {
        // Arrange
        _editor.Insertar('a');

        // Act: no hay carácter bajo el cursor al final
        _editor.BorrarDelante();

        // Assert
        _editor.Texto.Should().Be("a");
        _editor.Posicion.Should().Be(1);
    }

    #endregion

    #region Mover el cursor

    [Test]
    public void MoverIzquierda_Y_Derecha_MuevenElCursorUnaPosicion()
    {
        // Arrange
        _editor.Insertar('a');
        _editor.Insertar('n');
        _editor.Insertar('a');

        // Act y Assert: la izquierda no pasa de 0 ni la derecha del largo
        _editor.MoverIzquierda();
        _editor.Posicion.Should().Be(2);
        _editor.MoverIzquierda();
        _editor.MoverIzquierda();
        _editor.Posicion.Should().Be(0);
        _editor.MoverIzquierda(); // intento extra: límite
        _editor.Posicion.Should().Be(0);

        _editor.MoverDerecha();
        _editor.MoverDerecha();
        _editor.MoverDerecha();
        _editor.Posicion.Should().Be(3);
        _editor.MoverDerecha(); // intento extra: límite
        _editor.Posicion.Should().Be(3);
    }

    [Test]
    public void Inicio_Y_Fin_LlevanElCursorALosExtremos()
    {
        // Arrange
        _editor.Insertar('d');
        _editor.Insertar('a');
        _editor.Insertar('w');

        // Act y Assert
        _editor.Inicio();
        _editor.Posicion.Should().Be(0);
        _editor.Fin();
        _editor.Posicion.Should().Be(3);
    }

    #endregion

    #region Limpiar

    [Test]
    public void Limpiar_ConTextoYCursor_NoQuedaNada()
    {
        // Arrange
        _editor.Insertar('h');
        _editor.Insertar('o');
        _editor.Inicio();

        // Act
        _editor.Limpiar();

        // Assert
        _editor.Texto.Should().BeEmpty();
        _editor.Longitud.Should().Be(0);
        _editor.Posicion.Should().Be(0);
    }

    #endregion

    #region Pintado

    [Test]
    public void ParaPintar_AlFinal_MarcaUnEspacioResaltadoComoCursor()
    {
        // Arrange
        _editor.Insertar('a');
        _editor.Insertar('n');
        _editor.Insertar('a');

        // Act
        var markup = _editor.ParaPintar();

        // Assert: los tres caracteres seguidos y el cursor como espacio claro
        markup.Should().Be("ana[black on white] [/]");
    }

    [Test]
    public void ParaPintar_EnMedio_ResaltaElCaracterDebajoDelCursor()
    {
        // Arrange: "ana" con el cursor sobre la 'n'
        _editor.Insertar('a');
        _editor.Insertar('n');
        _editor.Insertar('a');
        _editor.MoverIzquierda();
        _editor.MoverIzquierda();

        // Act
        var markup = _editor.ParaPintar();

        // Assert: la 'n' sale resaltada y las otras dos normales
        markup.Should().Be("a[black on white]n[/]a");
    }

    [Test]
    public void ParaPintar_ConCorchetes_LosEscapaParaSpectre()
    {
        // Arrange: un texto con corchetes partiría el markup sin escapar
        _editor.Insertar('[');
        _editor.Insertar(']');

        // Act
        var markup = _editor.ParaPintar();

        // Assert: [[ y ]] son los corchetes literales de Spectre
        markup.Should().Contain("[[");
        markup.Should().Contain("]]");
    }

    #endregion
}
