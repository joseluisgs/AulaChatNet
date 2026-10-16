using AulaChatNet.Client.Terminal.Servicios;
using FluentAssertions;

namespace AulaChatNet.Client.Terminal.Tests.Servicios;

/// <summary>
/// Suite de tests para ResultadoConexion: el resultado tipado de conectar.
/// Es un record casi trivial... pero la regla <c>Exitoso</c> es la que usa
/// el bucle del arranque para decidir si sigue dentro o repregunta, y de
/// ella cuelga todo el programa — las reglas de esas se testean.
/// </summary>
[TestFixture]
public class ResultadoConexionTests
{
    [Test]
    public void Exitoso_SoloEsTrueCuandoNoHayError()
    {
        // Act & Assert: sin error = dentro; con error = fuera (venga de donde venga)
        new ResultadoConexion(null, HayServidor: true)
            .Exitoso.Should().BeTrue();

        new ResultadoConexion("Ya hay un Ana en #general", HayServidor: true)
            .Exitoso.Should().BeFalse();

        new ResultadoConexion("No se pudo conectar con el servidor: no responde", HayServidor: false)
            .Exitoso.Should().BeFalse();
    }
}
