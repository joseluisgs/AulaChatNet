using AulaChatNet.Shared.Comandos;
using FluentAssertions;

namespace AulaChatNet.Shared.Tests.Comandos;

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
    public void Parse_UsuariosSinArgumento_LlevaCanalNulo()
    {
        // Act: sin argumento = los conectados de TU canal
        var comando = ParserComandos.Parse("/usuarios");

        // Assert
        comando.Should().BeOfType<Comando.Usuarios>()
            .Which.NombreCanal.Should().BeNull();
    }

    [Test]
    [TestCase("/usuarios #daw")]
    [TestCase("/quienes daw")]    // alias y canal sin almohadilla: ambos valen
    public void Parse_UsuariosConArgumento_LlevaElCanal(string entrada)
    {
        // Act: con argumento = consulta a otra sala sin cambiar de canal
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Usuarios>()
            .Which.NombreCanal.Should().Be(entrada.Split(' ')[1]);
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

    #region Nick Tests

    [Test]
    [TestCase("/nick Ana2", "Ana2")]
    [TestCase("/NICK ana2", "ana2")]     // mayúsculas dan igual
    [TestCase("/nick   Ana2", "Ana2")]   // espacios sobrantes
    public void Parse_NickConNombre_DevuelveNick(string entrada, string esperado)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Nick>()
            .Which.Nombre.Should().Be(esperado);
    }

    [Test]
    public void Parse_NickSinNombre_DevuelveInvalidoConUso()
    {
        // Act
        var comando = ParserComandos.Parse("/nick");

        // Assert
        var invalido = comando.Should().BeOfType<Comando.Invalido>().Subject;
        invalido.Motivo.Should().Contain("nombre");
        invalido.Uso.Should().Contain("/nick");
    }

    #endregion

    #region Reconexión y sesión Tests

    [Test]
    [TestCase("/reconectar")]
    [TestCase("/reconnect")]     // alias en inglés
    [TestCase("/RECONECTAR")]     // mayúsculas dan igual
    public void Parse_Reconectar_DevuelveReconectar(string entrada)
    {
        // Act: la tecla R de la vista en vivo usa exactamente este comando
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Reconectar>();
    }

    [Test]
    public void Parse_SesionSinArgumento_PideVerLaSesion()
    {
        // Act: sin argumento = ver qué hay guardado en el perfil
        var comando = ParserComandos.Parse("/sesion");

        // Assert
        comando.Should().BeOfType<Comando.Sesion>()
            .Which.Accion.Should().Be(AccionSesion.Ver);
    }

    [Test]
    [TestCase("/sesion borrar")]
    [TestCase("/sesion eliminar")]   // sinónimos aceptados
    [TestCase("/sesion olvidar")]
    [TestCase("/SESION BORRAR")]     // mayúsculas dan igual
    public void Parse_SesionConBorrar_PideBorrarLaSesion(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Sesion>()
            .Which.Accion.Should().Be(AccionSesion.Borrar);
    }

    [Test]
    public void Parse_SesionConAccionDesconocida_DevuelveInvalidoConUso()
    {
        // Act
        var comando = ParserComandos.Parse("/sesion magia");

        // Assert
        var invalido = comando.Should().BeOfType<Comando.Invalido>().Subject;
        invalido.Motivo.Should().Contain("sesion");
        invalido.Uso.Should().Contain("/sesion borrar");
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

    #region Privado Tests

    [Test]
    [TestCase("/privado Ana ¿nos vemos?")]
    [TestCase("/mp Ana ¿nos vemos?")]
    [TestCase("/DM Ana ¿nos vemos?")] // mayúsculas: como todos los comandos
    public void Parse_PrivadoConDestinatarioYTexto_DevuelvePrivado(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert
        comando.Should().BeOfType<Comando.Privado>()
            .Which.Destinatario.Should().Be("Ana");
    }

    [Test]
    public void Parse_Privado_ElTextoConservaSusEspaciosInternos()
    {
        // Act: el texto es TODO lo que va tras el primer espacio
        var comando = ParserComandos.Parse("/privado Luis  nos   vemos mañana ");

        // Assert
        comando.Should().BeOfType<Comando.Privado>()
            .Which.Texto.Should().Be("nos   vemos mañana");
    }

    [Test]
    [TestCase("/privado")]
    [TestCase("/privado   ")]
    [TestCase("/mp")]
    public void Parse_PrivadoSinDestinatario_DevuelveInvalidoConElUso(string entrada)
    {
        // Act
        var comando = ParserComandos.Parse(entrada);

        // Assert: no se manda nada a la red sin destino, y el usuario aprende
        // a escribirlo sin tener que abrir la ayuda entera
        var invalido = comando.Should().BeOfType<Comando.Invalido>().Subject;
        invalido.Uso.Should().Contain("/privado <quien> <texto>");
    }

    [Test]
    public void Parse_PrivadoSinTexto_DevuelveInvalidoConElUso()
    {
        // Act: solo el destinatario, sin nada que decirle
        var comando = ParserComandos.Parse("/privado Ana");

        // Assert
        var invalido = comando.Should().BeOfType<Comando.Invalido>().Subject;
        invalido.Uso.Should().Contain("/privado <quien> <texto>");
    }

    [Test]
    [TestCase("/privado", true)]
    [TestCase("/privado Ana hola", true)]
    [TestCase("   /mp Ana hola", true)] // con espacios delante, como al teclear
    [TestCase("/DM Ana hola", true)]
    [TestCase("/dm", true)]
    [TestCase("/mpp hola", false)]     // palabra parecida, pero NO es un alias
    [TestCase("/privadox", false)]     // el alias debe ser una palabra completa
    [TestCase("hola", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void EmpiezaPrivado_DistintasLineas_DiceSiVaASerPrivado(string? entrada, bool esperado)
    {
        // Act: la web la usa para enseñar la nota ANTES de que exista el destino
        var resultado = ParserComandos.EmpiezaPrivado(entrada);

        // Assert
        resultado.Should().Be(esperado);
    }

    [Test]
    [TestCase("/privado Luis", "Luis")]
    [TestCase("/privado Luis hola", "Luis")]  // el texto puede llevar espacios
    [TestCase("  /mp   Luis   ¿nos vemos?", "Luis")]
    [TestCase("/DM Luis hola", "Luis")]
    [TestCase("/privado", null)]              // aún no hay a quién escribirle
    [TestCase("/privado ", null)]
    [TestCase("hola", null)]
    [TestCase(null, null)]
    public void DestinatarioDePrivado_DistintasLineas_DevuelveElPrimero(string? entrada, string? esperado)
    {
        // Act: mientras se teclea, Parse aún devuelve "inválido", y el chip
        // de la web necesita el nombre ANTES de que el comando esté completo
        var resultado = ParserComandos.DestinatarioDePrivado(entrada);

        // Assert
        resultado.Should().Be(esperado);
    }

    #endregion
}
