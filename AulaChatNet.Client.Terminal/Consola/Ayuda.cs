using Spectre.Console;
using Spectre.Console.Rendering;

namespace AulaChatNet.Client.Terminal.Consola;

/// <summary>
/// Ayuda del cliente: la tabla de comandos que se muestra al arrancar y
/// con "/ayuda". La lista de comandos vive en UN sitio, así que añadir un
/// comando nuevo solo exige tocar este fichero (y el parser).
///
/// <remarks>
/// Ojo con el markup de Spectre: los corchetes literales de los argumentos
/// ("/ayuda [comando]") hay que escaparlos con <c>Markup.Escape</c>, o el
/// markup los interpretaría como una etiqueta de estilo y estallaría.
/// </remarks>
/// </summary>
public static class Ayuda
{
    /// <summary>Comandos y su descripción, en el orden en que se muestran.</summary>
    private static readonly (string Comando, string Descripcion)[] Comandos =
    [
        ("/ayuda [comando]", "Esta ayuda, o la de un comando concreto"),
        ("/canales", "Lista los canales disponibles"),
        ("/canal <nombre>", "Cambia de canal (p. ej. /canal daw)"),
        ("/nick <nombre>", "Cambia tu nombre en caliente (p. ej. /nick Ana2)"),
        ("/usuarios [canal]", "Conectados de tu canal o de otro (p. ej. /usuarios daw)"),
        ("/reconectar", "Vuelve a entrar si se perdió la conexión (en vivo, también la tecla R)"),
        ("/sesion [borrar]", "Ver la sesión guardada o borrarla (útil en aulas compartidas)"),
        ("/limpiar", "Limpia la pantalla (el historial lo repinta el servidor)"),
        ("/salir", "Desconectar y cerrar (Ctrl+C hace lo mismo)")
    ];

    /// <summary>Tabla completa de comandos (al arrancar y con /ayuda).</summary>
    /// <returns>La tabla lista para pintar con AnsiConsole.Write o meterla en el lienzo en vivo.</returns>
    public static IRenderable CrearGeneral()
    {
        var tabla = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TemaDiscord.Input)
            .AddColumn(new TableColumn("[grey]Comando[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Qué hace[/]"));

        foreach (var (comando, descripcion) in Comandos)
            tabla.AddRow(
                $"[{TemaDiscord.Blurple.ToMarkup()}]{Markup.Escape(comando)}[/]",
                descripcion);

        return tabla;
    }

    /// <summary>Muestra la tabla completa de comandos (al arrancar y con /ayuda).</summary>
    public static void MostrarGeneral()
    {
        AnsiConsole.Write(CrearGeneral());
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Ayuda de UN comando (p. ej. "/ayuda canal"); si no existe, avisa y
    /// recuerda la ayuda general.
    /// </summary>
    /// <param name="tema">Comando del que se pide ayuda (sin barra o con ella).</param>
    /// <returns>Contenido listo para pintar con AnsiConsole.Write o meter en el lienzo en vivo.</returns>
    public static IRenderable CrearTema(string? tema)
    {
        if (tema is null)
            return CrearGeneral();

        // Se busca ignorando la barra inicial: "/canal" y "canal" son lo mismo
        var buscado = tema.Trim().TrimStart('/');
        var encontrados = Comandos
            .Where(c => c.Comando.StartsWith($"/{buscado}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (encontrados.Length == 0)
        {
            return new Markup(
                $"[yellow]No hay ayuda para '{Markup.Escape(tema)}'.[/] " +
                "Escribe [grey]/ayuda[/] para ver todos los comandos.");
        }

        return new Rows(encontrados
            .Select(c => new Markup(
                $"[{TemaDiscord.Blurple.ToMarkup()}]{Markup.Escape(c.Comando)}[/] — {c.Descripcion}"))
            .ToArray());
    }

    /// <summary>
    /// Muestra la ayuda de UN comando (p. ej. "/ayuda canal"); si no existe,
    /// avisa y recuerda la ayuda general.
    /// </summary>
    /// <param name="tema">Comando del que se pide ayuda (sin barra o con ella).</param>
    public static void MostrarTema(string? tema)
    {
        AnsiConsole.Write(CrearTema(tema));
        AnsiConsole.WriteLine();
    }
}
