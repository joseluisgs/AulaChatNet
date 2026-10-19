using AulaChatNet.Shared.Comandos;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Comandos;

/// <summary>
/// Suite de tests para AyudaComandos: el catálogo de comandos que leen los
/// DOS clientes. Se prueba porque es texto que el alumno LEE: si un comando
/// se cae de la ayuda, o si la nota de los privados deja de decir lo que
/// de verdad pasa, el chat enseña algo que ya no es verdad.
/// </summary>
[TestFixture]
public class AyudaComandosTests
{
    [Test]
    public void Comandos_IncluyeElDePrivado()
    {
        // Act
        var sintaxis = AyudaComandos.Comandos.Select(c => c.Sintaxis);

        // Assert
        sintaxis.Should().Contain("/privado <quien> <texto>");
    }

    [Test]
    public void Comandos_IncluyeElDeReaccion()
    {
        // Act: esta fila es la que pinta el menú de ARRANQUE de la terminal
        // y la ayuda de la web: si no está, el alumno nunca descubre el comando
        var sintaxis = AyudaComandos.Comandos.Select(c => c.Sintaxis);

        // Assert
        sintaxis.Should().Contain("/reaccion <emoji>");
    }

    [Test]
    public void Comandos_TodosLlevanSintaxisYDescripcion()
    {
        // Assert: un comando sin descripción es un comando que nadie va a
        // entender; uno sin sintaxis, que nadie va a saber escribir
        AyudaComandos.Comandos.Should().OnlyContain(c =>
            !string.IsNullOrWhiteSpace(c.Sintaxis)
            && !string.IsNullOrWhiteSpace(c.Descripcion));
    }

    [Test]
    public void Buscar_TemaPrivado_EncuentraElComandoYAdvierte()
    {
        // Act: lo que responde "/ayuda privado"
        var encontrados = AyudaComandos.Buscar("privado");

        // Assert: uno solo, y su descripción avisa de que no se guarda
        encontrados.Should().ContainSingle()
            .Which.Descripcion.Should().Contain("no se guarda");
    }

    [Test]
    public void Buscar_TemaReaccion_EncuentraElComandoYSuAlternar()
    {
        // Act: lo que responde "/ayuda reaccion"
        var encontrados = AyudaComandos.Buscar("reaccion");

        // Assert: uno solo, y su descripción explica que se puede quitar
        encontrados.Should().ContainSingle()
            .Which.Descripcion.Should().Contain("lo quita");
    }

    [Test]
    public void Buscar_TemaDesconocido_DevuelveVacioParaQueElClienteAviso()
    {
        // Act
        var encontrados = AyudaComandos.Buscar("reacciones");

        // Assert: vacío, no excepción — quien lo llama ya sabe avisar
        encontrados.Should().BeEmpty();
    }

    [Test]
    public void NotaPrivados_AvisaDeQueNoSeGuardanNiAparecenEnElHistorial()
    {
        // Assert: es la MISMA frase que enseñan la terminal y la web
        AyudaComandos.NotaPrivados.Should().Contain("no se guardan");
    }
}
