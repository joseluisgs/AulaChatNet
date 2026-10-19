using AulaChatNet.Client.Terminal.Consola;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using FluentAssertions;
using Spectre.Console;

namespace AulaChatNet.Client.Terminal.Tests.Consola;

/// <summary>
/// Suite de tests para VistaChat: cómo se PINTA una fila en la consola.
/// Son cadenas de markup, pero se prueban igual que cualquier otra lógica:
/// si el formato se rompe, la terminal se queda en blanco o pinta etiquetas
/// a la vista del usuario, y eso se ve en PRIMER lugar en los tests.
/// </summary>
[TestFixture]
public class VistaChatTests
{
    /// <summary>Momento fijo para los tests: 2 de marzo de 2026, 10:30 UTC.</summary>
    private static readonly DateTime Momento = new(2026, 3, 2, 10, 30, 0, DateTimeKind.Utc);

    /// <summary>La hora LOCAL que debe salir en pantalla (misma conversión que el código).</summary>
    private static string HoraLocal => Momento.ToLocalTime().ToString("HH:mm");

    /// <summary>Crea un mensaje del canal de prueba.</summary>
    private static MensajeDto Mensaje(string usuario, string texto, TipoMensaje tipo = TipoMensaje.Normal) =>
        new(Guid.NewGuid(), usuario, texto, Momento, tipo);

    /// <summary>Crea un mensaje privado de prueba.</summary>
    private static MensajePrivadoDto Privado(string emisor, string destinatario, string texto) =>
        new(Guid.NewGuid(), emisor, destinatario, texto, Momento);

    #region LineaMensajeMarkup Tests

    [Test]
    public void LineaMensajeMarkup_MensajeNormal_LlevaHoraInicialesYNombre()
    {
        // Act
        var linea = VistaChat.LineaMensajeMarkup(Mensaje("Ana", "Buenas a todos"));

        // Assert: hora local, iniciales entre corchetes y nombre con su color
        // (el ":" va FUERA de la etiqueta de color, por eso se busca "Ana[/]:")
        linea.Should().Contain(HoraLocal);
        linea.Should().Contain($"[[{TemaDiscord.Iniciales("Ana")}]]");
        linea.Should().Contain("Ana[/]:");
    }

    [Test]
    public void LineaMensajeMarkup_MensajeDeSistema_LoDibujaEnGrisCursiva()
    {
        // Act
        var linea = VistaChat.LineaMensajeMarkup(
            Mensaje("Ana", "Ana se ha unido al canal", TipoMensaje.Sistema));

        // Assert: sin iniciales ni nombre: los de sistema son otra cosa
        linea.Should().Contain("[grey italic]");
        linea.Should().NotContain("[[");
    }

    #endregion

    #region Reacciones Tests

    [Test]
    public void LineaMensajeMarkup_ConReacciones_PintaElChipConContadorYNombres()
    {
        // Arrange
        var mensaje = Mensaje("Ana", "Buenas a todos");
        var reacciones = new Dictionary<Guid, ReaccionesMensajeDto>
        {
            [mensaje.Id] = new(mensaje.Id,
            [
                new ReaccionDto("👍", ["Ana", "Bruno"]),
                new ReaccionDto("😂", ["Luis"])
            ])
        };

        // Act
        var linea = VistaChat.LineaMensajeMarkup(mensaje, reacciones);

        // Assert: los chips van al final, en gris y con la frase compartida
        linea.Should().Contain("👍 2 (Ana, Bruno)");
        linea.Should().Contain("😂 1 (Luis)");
        linea.Should().Contain("[grey]");
    }

    [Test]
    public void LineaMensajeMarkup_SinReaccionesParaEseMensaje_LaLineaNoLlevaChip()
    {
        // Arrange: hay reacciones, pero de OTRO mensaje
        var mensaje = Mensaje("Ana", "Buenas a todos");
        var reacciones = new Dictionary<Guid, ReaccionesMensajeDto>
        {
            [Guid.NewGuid()] = new(Guid.NewGuid(), [new ReaccionDto("👍", ["Luis"])])
        };

        // Act
        var linea = VistaChat.LineaMensajeMarkup(mensaje, reacciones);

        // Assert: el chip de nadie se cuela en una fila que no es la suya
        linea.Should().NotContain("👍");
    }

    [Test]
    public void LineaMensajeMarkup_ConNombresConCorchetes_LosEscapa()
    {
        // Arrange: en Spectre, un "[" sin escapar se comería el formato
        var mensaje = Mensaje("Ana", "Buenas a todos");
        var reacciones = new Dictionary<Guid, ReaccionesMensajeDto>
        {
            [mensaje.Id] = new(mensaje.Id, [new ReaccionDto("👍", ["[Ana]"])])
        };

        // Act
        var linea = VistaChat.LineaMensajeMarkup(mensaje, reacciones);

        // Assert: [[ y ]] son los corchetes literales de Spectre (mismo
        // escape que usa EditorLinea), y el nombre sale a la vista del usuario
        linea.Should().Contain("[[Ana]]");
    }

    [Test]
    public void LineaMensajeMarkup_MensajeDeSistema_NuncaLlevaChips()
    {
        // Arrange: defensa en profundidad — aunque alguien pasara un mapa
        // con entrada para un aviso, los de sistema no se pintan reaccionados
        var aviso = Mensaje("Ana", "Ana se ha unido al canal", TipoMensaje.Sistema);
        var reacciones = new Dictionary<Guid, ReaccionesMensajeDto>
        {
            [aviso.Id] = new(aviso.Id, [new ReaccionDto("👍", ["Luis"])])
        };

        // Act
        var linea = VistaChat.LineaMensajeMarkup(aviso, reacciones);

        // Assert
        linea.Should().NotContain("👍");
    }

    [Test]
    public void LineaMarkup_FilaPrivada_IgnoraLasReacciones()
    {
        // Arrange: los privados no se reaccionan, y el mapa no debe tocarlos
        var privado = Privado("Ana", "Luis", "¿nos vemos?");
        var reacciones = new Dictionary<Guid, ReaccionesMensajeDto>
        {
            [privado.Id] = new(privado.Id, [new ReaccionDto("👍", ["Luis"])])
        };

        // Act
        var linea = VistaChat.LineaMarkup(new FilaChat(true, null, privado), yo: "Ana", reacciones);

        // Assert
        linea.Should().NotContain("👍");
    }

    #endregion

    #region LineaPrivadoMarkup Tests

    [Test]
    public void LineaPrivadoMarkup_CuandoLoEscribiYo_DiceParaQuienEs()
    {
        // Act
        var linea = VistaChat.LineaPrivadoMarkup(Privado("Ana", "Luis", "¿nos vemos?"), yo: "Ana");

        // Assert: el 🔒 en rosa y el contexto "para Luis"
        linea.Should().Contain("🔒");
        linea.Should().Contain("para Luis");
        linea.Should().NotContain("de ");
    }

    [Test]
    public void LineaPrivadoMarkup_CuandoMeLoEnviaron_DiceQuienLoEscribio()
    {
        // Act
        var linea = VistaChat.LineaPrivadoMarkup(Privado("Ana", "Luis", "¿nos vemos?"), yo: "Luis");

        // Assert
        linea.Should().Contain("de Ana");
        linea.Should().NotContain("para ");
    }

    [Test]
    public void LineaPrivadoMarkup_NombreConDistintasMayusculas_SigueSiendoMio()
    {
        // Act: el servidor normaliza el nombre al registrar, pero la UI no
        // debe romperse si llega con otras mayúsculas
        var linea = VistaChat.LineaPrivadoMarkup(Privado("ana", "Luis", "hola"), yo: "Ana");

        // Assert
        linea.Should().Contain("para Luis");
    }

    [Test]
    public void LineaPrivadoMarkup_ConCorchetesEnElTexto_LosDejaInofensivos()
    {
        // Arrange: un mensaje con "[rojo]" no debe teñir la terminal entera
        // de rojo: Spectre trata lo que va entre corchetes como formato
        const string texto = "[rojo]hola[/]";

        // Act
        var linea = VistaChat.LineaPrivadoMarkup(Privado("Ana", "Luis", texto), yo: "Ana");

        // Assert: el texto sale ESCAPADO, no tal cual. (Si alguien quita el
        // Markup.Escape de VistaChat, el texto crudo ya no coincide y el
        // test salta: ese es su trabajo.)
        linea.Should().Contain(Markup.Escape(texto));
    }

    #endregion

    #region LineaMarkup Tests

    [Test]
    public void LineaMarkup_FilaDeCanal_PintaComoMensajeDeCanal()
    {
        // Arrange
        var fila = new FilaChat(false, Mensaje("Ana", "Buenas"), null);

        // Act
        var linea = VistaChat.LineaMarkup(fila, "Ana");

        // Assert
        linea.Should().NotContain("🔒");
        linea.Should().Contain("Ana[/]: Buenas");
    }

    [Test]
    public void LineaMarkup_FilaPrivada_PintaComoPrivado()
    {
        // Arrange
        var fila = new FilaChat(true, null, Privado("Ana", "Luis", "Confidencia"));

        // Act
        var linea = VistaChat.LineaMarkup(fila, "Ana");

        // Assert
        linea.Should().Contain("🔒");
        linea.Should().Contain("Confidencia");
    }

    #endregion

    #region LineaEscribiendoMarkup Tests

    [Test]
    public void LineaEscribiendoMarkup_SinNadie_DevuelveCadenaVacia()
    {
        // Act
        var linea = VistaChat.LineaEscribiendoMarkup([]);

        // Assert: quien la usa no debe añadir una fila vacía a la zona
        linea.Should().BeEmpty();
    }

    [Test]
    public void LineaEscribiendoMarkup_UnaPersona_DiceQueEstaEscribiendoEnGris()
    {
        // Act
        var linea = VistaChat.LineaEscribiendoMarkup(["Ana"]);

        // Assert: gris y sin adornos a propósito — el indicador está para
        // saberlo, no para llevarse la mirada de la conversación
        linea.Should().Contain("Ana está escribiendo...");
        linea.Should().StartWith("[grey]");
    }

    [Test]
    public void LineaEscribiendoMarkup_DosPersonas_DiceQueEstanEscribiendo()
    {
        // Act
        var linea = VistaChat.LineaEscribiendoMarkup(["Ana", "Bruno"]);

        // Assert: la frase viene de Shared, así que es literalmente el
        // mismo texto que pinta la web
        linea.Should().Contain("Ana, Bruno están escribiendo...");
    }

    [Test]
    public void LineaEscribiendoMarkup_NombreConCorchetes_LosDejaInofensivos()
    {
        // Arrange: Spectre trata lo que va entre corchetes como formato
        var frase = EscribiendoTexto.Descripcion(["[rojo]"]);

        // Act
        var linea = VistaChat.LineaEscribiendoMarkup(["[rojo]"]);

        // Assert: escapada, igual que el resto de filas de la vista
        linea.Should().Contain(Markup.Escape(frase));
    }

    #endregion
}
