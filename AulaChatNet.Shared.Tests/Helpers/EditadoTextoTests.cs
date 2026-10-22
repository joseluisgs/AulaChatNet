using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Pruebas de la marca de editado: el texto PLANO que la terminal y la web
/// pintan tras el mensaje. El markup de cada cliente se prueba en SU suite.
/// </summary>
[TestFixture]
public class EditadoTextoTests
{
    [Test]
    public void Descripcion_MensajeEditado_DevuelveLaMarca()
    {
        // Act
        var marca = EditadoTexto.Descripcion(editado: true);

        // Assert: mismo "(editado)" que el de Discord, con el paréntesis
        // dentro para que nadie tenga que acordarse de añadirlo
        marca.Should().Be("(editado)");
    }

    [Test]
    public void Descripcion_MensajeSinEditar_DevuelveCadenaVacia()
    {
        // Act
        var marca = EditadoTexto.Descripcion(editado: false);

        // Assert: quien la recibe no pinta nada con una cadena vacía
        marca.Should().BeEmpty();
    }

    [Test]
    public void Marca_YDescripcionTrue_DicenLoMismo()
    {
        // Act + Assert: la constante y el método no pueden divergir — si
        // alguien cambia una, la otra tiene que fallar aquí
        EditadoTexto.Descripcion(editado: true).Should().Be(EditadoTexto.Marca);
    }
}
