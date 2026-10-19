using AulaChatNet.Shared.Helpers;
using FluentAssertions;
using Microsoft.Reactive.Testing;
using System.Reactive.Linq;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Pruebas del seguimiento de quién está escribiendo: qué nombres entran en
/// la lista, cuándo caduca cada marca y qué se emite al cambiar.
///
/// <remarks>
/// El tiempo corre con un reloj VIRTUAL (TestScheduler): los cinco segundos
/// de caducidad pasan al instante con Avanzar(...) y ningún test depende
/// de esperas reales ni de hilos. Ese es justamente el motivo de que la clase
/// acepte un IScheduler en lugar de leer DateTime.UtcNow a secas.
/// </remarks>
/// </summary>
[TestFixture]
public class SeguimientoEscrituraTests
{
    private TestScheduler _reloj = null!;

    [SetUp]
    public void SetUp() => _reloj = new TestScheduler();

    /// <summary>Crea un seguimiento atado al reloj virtual del test.</summary>
    private SeguimientoEscritura Crear() => new(_reloj);

    /// <summary>Adelanta el reloj virtual (ningún test espera tiempo real).</summary>
    private void Avanzar(TimeSpan tiempo) => _reloj.AdvanceTo(_reloj.Now.Ticks + tiempo.Ticks);

    #region Senalar Tests

    [Test]
    public void Senalar_PrimeraSenal_ActivaEseNombre()
    {
        // Arrange
        using var seguimiento = Crear();

        // Act
        seguimiento.Senalar("Ana");

        // Assert
        seguimiento.Vista.Should().Equal("Ana");
    }

    [Test]
    public void Senalar_DosVecesElMismo_NoLoDuplica()
    {
        // Arrange
        using var seguimiento = Crear();

        // Act: la segunda señal solo RENOVA la caducidad, no añade fila
        seguimiento.Senalar("Ana");
        seguimiento.Senalar("Ana");

        // Assert
        seguimiento.Vista.Should().Equal("Ana");
    }

    [Test]
    public void Senalar_NombreConDistintasMayusculas_TieneEnCuentaSoloAUna()
    {
        // Arrange: los nombres de un canal son únicos sin distinguir
        // mayúsculas, así que "Ana" y "ANA" son la misma persona
        using var seguimiento = Crear();

        // Act
        seguimiento.Senalar("Ana");
        seguimiento.Senalar("ANA");

        // Assert
        seguimiento.Vista.Should().Equal("Ana");
    }

    [Test]
    public void Senalar_VariosNombres_SalenEnOrdenAlfabetico()
    {
        // Arrange: el orden de llegada haría bailar los nombres cada vez que
        // alguien renueva, así que se ordena alfabéticamente
        using var seguimiento = Crear();

        // Act
        seguimiento.Senalar("Bruno");
        seguimiento.Senalar("Ana");
        seguimiento.Senalar("Clara");

        // Assert
        seguimiento.Vista.Should().Equal("Ana", "Bruno", "Clara");
    }

    [Test]
    public void Senalar_NombreVacio_NoCambiaNada()
    {
        // Arrange: la señal viene de la red y no debería llegar así, pero la
        // clase es pública y se defiende sola
        using var seguimiento = Crear();

        // Act
        seguimiento.Senalar("   ");

        // Assert
        seguimiento.Vista.Should().BeEmpty();
    }

    #endregion

    #region Caducidad Tests

    [Test]
    public void Latido_AntesDeCaducidad_LosMantiene()
    {
        // Arrange
        using var seguimiento = Crear();
        seguimiento.Senalar("Ana");

        // Act: cuatro segundos (la caducidad son cinco)
        Avanzar(TimeSpan.FromSeconds(4));

        // Assert
        seguimiento.Vista.Should().Equal("Ana");
    }

    [Test]
    public void Latido_PasadaLaCaducidad_LosDescarta()
    {
        // Arrange
        using var seguimiento = Crear();
        seguimiento.Senalar("Ana");

        // Act: a los cinco segundos exactos la marca ya es caducada
        Avanzar(SeguimientoEscritura.CaducidadPorDefecto);

        // Assert: sin nadie que la limpie — el servidor no guarda nada
        seguimiento.Vista.Should().BeEmpty();
    }

    [Test]
    public void Latido_SenalarDeNuevo_RenuevaElPlazo()
    {
        // Arrange
        using var seguimiento = Crear();
        seguimiento.Senalar("Ana");

        // Act: señal fresca a los 4 s → la marca pasa a caducar a los 9 s
        Avanzar(TimeSpan.FromSeconds(4));
        seguimiento.Senalar("Ana");
        Avanzar(TimeSpan.FromSeconds(4)); // t = 8 s: aún vive (8 - 4 < 5)

        // Assert intermedio
        seguimiento.Vista.Should().Equal("Ana");

        // Act: ya sin renovar (t = 10 s > 9 s)
        Avanzar(TimeSpan.FromSeconds(2));

        // Assert
        seguimiento.Vista.Should().BeEmpty();
    }

    #endregion

    #region Quitar y Limpiar Tests

    [Test]
    public void Quitar_AlguienConSenal_LoApagaAlInstante()
    {
        // Arrange: esto es lo que ocurre cuando llega un mensaje suyo
        using var seguimiento = Crear();
        seguimiento.Senalar("Ana");
        seguimiento.Senalar("Bruno");

        // Act
        seguimiento.Quitar("Ana");

        // Assert: sin esperar a la caducidad, que sería un parpadeo
        seguimiento.Vista.Should().Equal("Bruno");
    }

    [Test]
    public void Quitar_AlguienSinSenal_NoTocaNada()
    {
        // Arrange
        using var seguimiento = Crear();
        seguimiento.Senalar("Ana");

        // Act: un mensaje de sistema con autor "Servidor", por ejemplo
        seguimiento.Quitar("Servidor");

        // Assert
        seguimiento.Vista.Should().Equal("Ana");
    }

    [Test]
    public void Limpiar_VaciaTodo()
    {
        // Arrange: cambio de canal con la lista llena
        using var seguimiento = Crear();
        seguimiento.Senalar("Ana");
        seguimiento.Senalar("Bruno");

        // Act
        seguimiento.Limpiar();

        // Assert: nada de la sala anterior asoma en la nueva
        seguimiento.Vista.Should().BeEmpty();
    }

    #endregion

    #region Emisiones Tests

    [Test]
    public void Emision_SoloSeEmiteCuandoLaListaCambia()
    {
        // Arrange
        using var seguimiento = Crear();

        var emitidas = new List<IReadOnlyList<string>>();
        using var suscripcion = seguimiento.Activos
            .Skip(1) // el valor inicial vacío del sujeto no es una noticia
            .Subscribe(emitidas.Add);

        // Act
        seguimiento.Senalar("Ana");  // alguien nuevo → emite
        seguimiento.Senalar("Ana");  // solo renueva → NO debe emitir
        seguimiento.Senalar("Bruno"); // otro nuevo → emite
        Avanzar(SeguimientoEscritura.CaducidadPorDefecto); // los dos caducan → emite

        // Assert: tres cambios de verdad, ni uno más
        emitidas.Should().HaveCount(3);
        emitidas[0].Should().Equal("Ana");
        emitidas[1].Should().Equal("Ana", "Bruno");
        emitidas[2].Should().BeEmpty();
    }

    #endregion
}
