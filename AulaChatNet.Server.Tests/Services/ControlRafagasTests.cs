using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Configuracion;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Tests.Services;

/// <summary>
/// Pruebas de la ventana deslizante de ráfagas (Fase 4.1). El reloj se pasa
/// a mano en cada llamada: aquí no se espera NI UN segundo real, y aun así
/// se prueba la ventana entera.
/// </summary>
[TestFixture]
public class ControlRafagasTests
{
    /// <summary>Instante de referencia para todas las pruebas.</summary>
    private static readonly DateTime T0 = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Crea un control con la cuota indicada (por defecto 3 cada 5 s).</summary>
    private static ControlRafagas Crear(int mensajes = 3, int segundos = 5) =>
        new(Options.Create(new ChatConfig
        {
            RafagaMensajes = mensajes,
            RafagaVentanaSegundos = segundos
        }));

    #region Cuota y ventana Tests

    [Test]
    public void Intentar_HastaElMaximo_DevuelveTrueLosTres()
    {
        // Arrange
        var control = Crear(mensajes: 3);

        // Act
        var primero = control.Intentar("conn-1", T0);
        var segundo = control.Intentar("conn-1", T0.AddSeconds(1));
        var tercero = control.Intentar("conn-1", T0.AddSeconds(2));

        // Assert
        primero.Should().BeTrue("el primer mensaje siempre cabe");
        segundo.Should().BeTrue();
        tercero.Should().BeTrue("los tres caben en la cuota de tres");
    }

    [Test]
    public void Intentar_SobreElMaximo_DevuelveFalse()
    {
        // Arrange: la cuota de 2 queda gastada
        var control = Crear(mensajes: 2);
        control.Intentar("conn-1", T0);
        control.Intentar("conn-1", T0);

        // Act
        var tercero = control.Intentar("conn-1", T0);

        // Assert
        tercero.Should().BeFalse("el tercer mensaje dentro de la ventana no pasa");
    }

    [Test]
    public void Intentar_AntesDeCaducarLaVentana_SigueContando()
    {
        // Arrange: cuota de 1 gastada en T0; ventana de 5 segundos
        var control = Crear(mensajes: 1, segundos: 5);
        control.Intentar("conn-1", T0);

        // Act: a T0+4 s la marca de T0 todavía no caducó
        var otro = control.Intentar("conn-1", T0.AddSeconds(4));

        // Assert: esto distingue la ventana DESLIZANTE de un contador que
        // se reinicie cada N segundos fijos
        otro.Should().BeFalse("a los 4 s la marca de T0 sigue viva");
    }

    [Test]
    public void Intentar_PasadaLaVentana_VuelveAPermitir()
    {
        // Arrange: cuota de 1 gastada en T0; ventana de 5 segundos
        var control = Crear(mensajes: 1, segundos: 5);
        control.Intentar("conn-1", T0);

        // Act: pasado el margen, la marca caducó y la cuota se recarga
        var otro = control.Intentar("conn-1", T0.AddSeconds(5));

        // Assert
        otro.Should().BeTrue("a los 5 s la marca de T0 ya caducó");
    }

    #endregion

    #region Aislamiento entre conexiones Tests

    [Test]
    public void Intentar_ConexionesDistintas_NoCompartenLaCuota()
    {
        // Arrange: conn-1 gasta su cuota entera
        var control = Crear(mensajes: 1);
        control.Intentar("conn-1", T0);
        control.Intentar("conn-1", T0);

        // Act: conn-2 tiene SU ventana, intacta
        var deOtra = control.Intentar("conn-2", T0);

        // Assert: la clave es la conexión, no el aula entera
        deOtra.Should().BeTrue("la cuota es POR CONEXIÓN");
    }

    [Test]
    public void Intentar_LimiteCero_NoAplicaNingunControl()
    {
        // Arrange: 0 = sin límite (mismo criterio que RetencionDias)
        var control = Crear(mensajes: 0);

        // Act: veinte mensajes sin respirar
        var pasanTodos = true;
        for (var i = 0; i < 20; i++)
            pasanTodos &= control.Intentar("conn-1", T0);

        // Assert
        pasanTodos.Should().BeTrue("con cuota 0 nadie se queda sin escribir");
    }

    [Test]
    public void Limpiar_ConLaClave_BorraLaCuota()
    {
        // Arrange: cuota agotada y desconectada
        var control = Crear(mensajes: 1);
        control.Intentar("conn-1", T0);
        control.Intentar("conn-1", T0);
        control.Limpiar("conn-1");

        // Act: una conexión NUEVA (mismo id tras reconectar) empieza de cero
        var otraVez = control.Intentar("conn-1", T0);

        // Assert
        otraVez.Should().BeTrue("al desconectar se olvida la cuota");
    }

    #endregion
}
