using AulaChatNet.Client.Terminal.Comandos;
using FluentAssertions;

namespace AulaChatNet.Client.Terminal.Tests.Comandos;

/// <summary>
/// Suite de tests para ParserComandos: convierte lo que escribe el usuario
/// en un comando tipado. Es lógica pura, así que se prueba sin red, sin UI
/// y en milisegundos.
/// </summary>
[TestFixture]
public class ParserComandosTests
{
    #region Enviar (texto normal) Tests

    [Test]
    [TestCase("buenas a todos")]
    [TestCase("¿alguien ha hecho el GW?")]
    [TestCase("jajaja")]
    public void Parse_TextoSinBarra_DevuelveEnviar(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Enviar>()
            .Which.Texto.Should().Be(entrada);
    }

    [Test]
    public void Parse_TextoConEspaciosAlrededor_LosRecorta()
    {
        // Act
        var comando = ParserComandos.Parse("  buenos días  ");

        // Assert
        comando.Should().BeOfType<Comando.Enviar>()
            .Which.Texto.Should().Be("buenos días");
    }

    [Test]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Parse_VacioONulo_DevuelveEnviarVacio(string? entrada)
    {
        // Act: el prompt puede devolver línea vacía si solo se pulsó Enter
        var comando = ParserComandos.Parse(entrada);

        // Assert: se envía vacío y la validación (ChatConfig) lo rechazará
        comando.Should().BeOfType<Comando.Enviar>()
            .Which.Texto.Should().BeEmpty();
    }

    #endregion

    #region Canal Tests

    [Test]
    [TestCase("/canal daw", "daw")]
    [TestCase("/canal #daw", "#daw")]
    [TestCase("/CANAL daw", "daw")]           // mayúsculas dan igual
    [TestCase("/canal   daw", "daw")]         // espacios sobrantes
    [TestCase("  /canal daw", "daw")]         // espacios al principio
    [TestCase("/canal #general", "#general")]
    public void Parse_CanalConArgumento_DevuelveCanal(string entrada, string esperado)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Canal>()
            .Which.Nombre.Should().Be(esperado);
    }

    [Test]
    public void Parse_CanalSinArgumento_DevuelveInvalidoConUso()
    {
        // Act: alguien escribe "/canal" y no más
        var comando = ParserComandos.Parse("/canal");

        // Assert: motivo + ejemplo de uso, para poder ayudar sin abrir la ayuda
        var invalido = comando.Should().BeOfType<Comando.Invalido>().Subject;
        invalido.Motivo.Should().Contain("canal");
        invalido.Uso.Should().Contain("/canal");
    }

    #endregion

    #region Ayuda Tests

    [Test]
    [TestCase("/ayuda")]
    [TestCase("/help")]          // alias en inglés
    [TestCase("/AYUDA")]
    public void Parse_AyudaSinTema_DevuelveAyudaGeneral(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Ayuda>()
            .Which.Tema.Should().BeNull();
    }

    [Test]
    public void Parse_AyudaConTema_DevuelveElTema()
    {
        // Act
        var comando = ParserComandos.Parse("/ayuda canal");

        // Assert
        comando.Should().BeOfType<Comando.Ayuda>()
            .Which.Tema.Should().Be("canal");
    }

    #endregion

    #region Resto de comandos Tests

    [Test]
    public void Parse_Canales_DevuelveCanales()
    {
        // Act
        var comando = ParserComandos.Parse("/canales");

        // Assert
        comando.Should().BeOfType<Comando.Canales>();
    }

    [Test]
    [TestCase("/usuarios")]
    [TestCase("/quienes")]       // alias en español
    public void Parse_Usuarios_DevuelveUsuarios(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Usuarios>();
    }

    [Test]
    [TestCase("/limpiar")]
    [TestCase("/clear")]         // alias en inglés
    public void Parse_Limpiar_DevuelveLimpiar(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Limpiar>();
    }

    [Test]
    [TestCase("/salir")]
    [TestCase("/exit")]
    [TestCase("/quit")]
    public void Parse_Salir_DevuelveSalir(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Salir>();
    }

    #endregion

    #region Errores Tests

    [Test]
    [TestCase("/queesesto", "/queesesto")]
    [TestCase("/canalito daw", "/canalito")]   // solo la palabra del comando, sin argumento
    public void Parse_ComandoDesconocido_DevuelveDesconocidoConLaPalabra(string entrada, string esperado)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert: se guarda SOLO la palabra del comando para repetirla en el error
        comando.Should().BeOfType<Comando.Desconocido>()
            .Which.Palabra.Should().Be(esperado);
    }

    [Test]
    public void Parse_SoloUnaBarra_DevuelveDesconocido()
    {
        // Act: "/" a secas no es ningún comando
        var comando = ParserComandos.Parse("/");

        // Assert
        comando.Should().BeOfType<Comando.Desconocido>();
    }

    #endregion
}
