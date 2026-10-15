using Spectre.Console;

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
        ("/usuarios", "Muestra los conectados del canal actual"),
        ("/limpiar", "Limpia la pantalla (el historial lo repinta el servidor)"),
        ("/salir", "Desconectar y cerrar (Ctrl+C hace lo mismo)")
    ];

    /// <summary>Muestra la tabla completa de comandos (al arrancar y con /ayuda).</summary>
    public static void MostrarGeneral()
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

        AnsiConsole.Write(tabla);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Muestra la ayuda de UN comando (p. ej. "/ayuda canal"); si no existe,
    /// avisa y recuerda la ayuda general.
    /// </summary>
    /// <param name="tema">Comando del que se pide ayuda (sin barra o con ella).</param>
    public static void MostrarTema(string? tema)
    {
        if (tema is null)
        {
            MostrarGeneral();
            return;
        }

        // Se busca ignorando la barra inicial: "/canal" y "canal" son lo mismo
        var buscado = tema.Trim().TrimStart('/');
        var encontrados = Comandos
            .Where(c => c.Comando.StartsWith($"/{buscado}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (encontrados.Length == 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]No hay ayuda para '{Markup.Escape(tema)}'.[/] " +
                "Escribe [grey]/ayuda[/] para ver todos los comandos.");
            AnsiConsole.WriteLine();

            return;
        }

        foreach (var (comando, descripcion) in encontrados)
            AnsiConsole.MarkupLine(
                $"[{TemaDiscord.Blurple.ToMarkup()}]{Markup.Escape(comando)}[/] — {descripcion}");

        AnsiConsole.WriteLine();
    }
}
