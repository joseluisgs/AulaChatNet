using AulaChatNet.Shared.Helpers;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Pruebas de la detección de menciones (@ana). Es la MISMA regla que
/// aplican el servidor (avisar) y los dos clientes (resaltar), así que
/// estas pruebas blindan el contrato de las tres partes a la vez.
/// </summary>
[TestFixture]
public class MencionesTests
{
    #region Extraer Tests

    [Test]
    public void Extraer_UnaMencion_DevuelveElNombreSinArroba()
    {
        // Act
        var nombres = Menciones.Extraer("hola @Ana, ¿vienes?");

        // Assert
        nombres.Should().Equal("Ana");
    }

    [Test]
    public void Extraer_DosMenciones_DevuelveLasDosEnOrden()
    {
        // Act
        var nombres = Menciones.Extraer("@Ana @Luis, ¿veis esto?");

        // Assert
        nombres.Should().Equal("Ana", "Luis");
    }

    [Test]
    public void Extraer_MismaPersonaRepetida_NoLaDuplica()
    {
        // Act: la misma persona dos veces, además con otra grafía
        var nombres = Menciones.Extraer("@Ana hola @ana, ¿repites?");

        // Assert: solo la primera grafía, sin duplicar por las mayúsculas
        nombres.Should().Equal("Ana");
    }

    [Test]
    public void Extraer_ConPuntuacionPegada_CortaElToken()
    {
        // Act: la coma corta el token, como en cualquier chat
        var nombres = Menciones.Extraer("@Ana, ¿vienes?");

        // Assert: sale "Ana", no "Ana,"
        nombres.Should().Equal("Ana");
    }

    [Test]
    public void Extraer_SinArrobas_ListaVacia()
    {
        Menciones.Extraer("Buenas a todos").Should().BeEmpty();
    }

    [Test]
    public void Extraer_Nulo_ListaVacia()
    {
        Menciones.Extraer(null).Should().BeEmpty();
    }

    #endregion

    #region Contiene Tests

    [Test]
    public void Contiene_MismoNombreEnOtrasMayusculas_True()
    {
        // Assert: la misma regla que nicks y canales, insensible a mayúsculas
        Menciones.Contiene("hola @ana", "Ana").Should().BeTrue();
    }

    [Test]
    public void Contiene_NombreNoMencionado_False()
    {
        Menciones.Contiene("hola @Ana", "Luis").Should().BeFalse();
    }

    [Test]
    public void Contiene_ArrobaSinNombreDetras_False()
    {
        // "@" suelta, o pegada a algo que no forma nombre, no menciona a nadie
        Menciones.Contiene("cuenta @ y sigue", "Ana").Should().BeFalse();
    }

    #endregion

    #region Trocear Tests

    [Test]
    public void Trocear_MezclaDeTextoYMenciones_SeparaEnOrden()
    {
        // Act
        var tramos = Menciones.Trocear("hola @Ana y @Luis, ¿vais?");

        // Assert: texto, mención, texto, mención, texto — en el orden del original
        tramos.Select(t => (t.Texto, t.EsMencion)).Should().Equal(
            ("hola ", false),
            ("@Ana", true),
            (" y ", false),
            ("@Luis", true),
            (", ¿vais?", false));
    }

    [Test]
    public void Trocear_SinMenciones_UnSoloTramoDeTexto()
    {
        // Act
        var tramos = Menciones.Trocear("Buenas a todos");

        // Assert
        tramos.Should().ContainSingle()
            .Which.EsMencion.Should().BeFalse("sin @ no hay nada que resaltar");
    }

    #endregion
}
