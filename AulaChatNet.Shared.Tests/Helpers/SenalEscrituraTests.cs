using AulaChatNet.Shared.Helpers;
using FluentAssertions;
using Microsoft.Reactive.Testing;

namespace AulaChatNet.Shared.Tests.Helpers;

/// <summary>
/// Pruebas del muestreo de señales de escritura: cuántas veces se avisa al
/// servidor por ventana y qué corta una señal. Usa un reloj VIRTUAL
/// (TestScheduler), así que un segundo de muestreo pasa al instante.
/// </summary>
[TestFixture]
public class SenalEscrituraTests
{
    private TestScheduler _reloj = null!;
    private int _enviadas;

    [SetUp]
    public void SetUp()
    {
        _reloj = new TestScheduler();
        _enviadas = 0;
    }

    /// <summary>Crea la señal con un "enviar" que solo cuenta llamadas.</summary>
    private SenalEscritura Crear() => new(Contar, _reloj);

    /// <summary>Lo que en producción manda la señal al hub: aquí, solo contar.</summary>
    private Task Contar()
    {
        _enviadas++;
        return Task.CompletedTask;
    }

    /// <summary>Adelanta el reloj virtual (ningún test espera tiempo real).</summary>
    private void Avanzar(TimeSpan tiempo) => _reloj.AdvanceTo(_reloj.Now.Ticks + tiempo.Ticks);

    #region Muestreo Tests

    [Test]
    public void UnaTeclaYPasarElPeriodo_SeSenalaUnaVez()
    {
        // Arrange
        using var senal = Crear();

        // Act
        senal.Tecla();
        Avanzar(SenalEscritura.PeriodoPorDefecto);

        // Assert: la primera señal sale al cerrarse la ventana
        _enviadas.Should().Be(1);
    }

    [Test]
    public void VariasTeclasEnLaMismaVentana_SeSenalaUnaSolaVez()
    {
        // Arrange: teclear rápido no puede significar una llamada por tecla
        using var senal = Crear();

        // Act
        senal.Tecla();
        senal.Tecla();
        senal.Tecla();
        Avanzar(SenalEscritura.PeriodoPorDefecto);

        // Assert
        _enviadas.Should().Be(1);
    }

    [Test]
    public void TeclasEnVentanasDistintas_SenalaUnaVezPorVentana()
    {
        // Arrange: mientras se siga escribiendo, el indicador se RENOVA
        using var senal = Crear();

        // Act
        senal.Tecla();
        Avanzar(SenalEscritura.PeriodoPorDefecto);
        senal.Tecla();
        Avanzar(SenalEscritura.PeriodoPorDefecto);

        // Assert
        _enviadas.Should().Be(2);
    }

    [Test]
    public void SinTeclas_NoSeSenalaNada()
    {
        // Arrange: el tiempo pasa, pero sin actividad no hay señales
        using var senal = Crear();

        // Act
        Avanzar(TimeSpan.FromSeconds(3));

        // Assert
        _enviadas.Should().Be(0);
    }

    #endregion

    #region Envio Tests

    [Test]
    public void EnvioEnLaMismaVentana_CortaLaSenal()
    {
        // Arrange: teclear y mandar el mensaje dentro del mismo segundo
        using var senal = Crear();

        // Act
        senal.Tecla();
        senal.Envio();
        Avanzar(SenalEscritura.PeriodoPorDefecto);

        // Assert: si saliera, el receptor repondría al autor en la lista de
        // "escribiendo" justo DESPUÉS de que su mensaje ya estuviese en pantalla
        _enviadas.Should().Be(0);
    }

    [Test]
    public void EnvioEnLaVentanaSiguiente_TambienCorta()
    {
        // Arrange: señal ya emitida y, después, un envío
        using var senal = Crear();

        // Act
        senal.Tecla();
        Avanzar(SenalEscritura.PeriodoPorDefecto);
        senal.Envio();
        Avanzar(SenalEscritura.PeriodoPorDefecto);

        // Assert: la ventana posterior queda sin emitir
        _enviadas.Should().Be(1);
    }

    [Test]
    public void TeclaDespuesDeUnEnvio_VuelveASenalar()
    {
        // Arrange: mandó un mensaje y sigue escribiendo otro
        using var senal = Crear();

        // Act
        senal.Tecla();
        Avanzar(SenalEscritura.PeriodoPorDefecto); // señal 1
        senal.Envio();
        Avanzar(SenalEscritura.PeriodoPorDefecto); // ventana cortada
        senal.Tecla();
        Avanzar(SenalEscritura.PeriodoPorDefecto); // señal 2

        // Assert
        _enviadas.Should().Be(2);
    }

    #endregion
}
