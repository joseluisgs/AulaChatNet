using AulaChatNet.Shared.Comandos;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AulaChatNet.Client.Terminal.Consola;

/// <summary>
/// Ayuda del cliente terminal: pinta la tabla de comandos que se muestra al
/// arrancar y con "/ayuda". Las FILAS viven en <see cref="AyudaComandos"/>
/// (Shared: la MISMA lista que pinta la web), así que añadir un comando es
/// tocar el parser, el catálogo y —si hace falta— el render de aquí. Lo
/// propio de la terminal (los atajos de teclado) se añade en este fichero.
///
/// <remarks>
/// Ojo con el markup de Spectre: los corchetes literales de los argumentos
/// ("/ayuda [comando]") hay que escaparlos con <c>Markup.Escape</c>, o el
/// markup los interpretaría como una etiqueta de estilo y estallaría.
/// </remarks>
/// </summary>
public static class Ayuda
{
    /// <summary>
    /// Tabla completa de comandos (al arrancar y con /ayuda), con el pie de
    /// los atajos propios de la terminal.
    /// </summary>
    /// <returns>La tabla lista para pintar con AnsiConsole.Write o meterla en el lienzo en vivo.</returns>
    public static IRenderable CrearGeneral()
    {
        var tabla = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TemaDiscord.Input)
            .AddColumn(new TableColumn("[grey]Comando[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Qué hace[/]"));

        foreach (var fila in AyudaComandos.Comandos)
            tabla.AddRow(
                $"[{TemaDiscord.Blurple.ToMarkup()}]{Markup.Escape(fila.Sintaxis)}[/]",
                fila.Descripcion);

        // Lo propio de ESTE cliente: la web no tiene esas teclas
        return new Rows(
            tabla,
            new Markup("[grey]Atajos: R = /reconectar (con la línea vacía) · Ctrl+C = /salir[/]"));
    }

    /// <summary>Muestra la tabla completa de comandos (al arrancar y con /ayuda).</summary>
    public static void MostrarGeneral()
    {
        AnsiConsole.Write(CrearGeneral());
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Ayuda de UN comando (p. ej. "/ayuda canal"); si no existe, avisa y
    /// recuerda la ayuda general. La búsqueda es la MISMA que hace la web:
    /// por prefijo en el catálogo compartido, ignorando la barra inicial.
    /// </summary>
    /// <param name="tema">Comando del que se pide ayuda (sin barra o con ella).</param>
    /// <returns>Contenido listo para pintar con AnsiConsole.Write o meter en el lienzo en vivo.</returns>
    public static IRenderable CrearTema(string? tema)
    {
        if (tema is null)
            return CrearGeneral();

        var encontrados = AyudaComandos.Buscar(tema);

        if (encontrados.Count == 0)
        {
            return new Markup(
                $"[yellow]No hay ayuda para '{Markup.Escape(tema)}'.[/] " +
                "Escribe [grey]/ayuda[/] para ver todos los comandos.");
        }

        return new Rows(encontrados
            .Select(c => new Markup(
                $"[{TemaDiscord.Blurple.ToMarkup()}]{Markup.Escape(c.Sintaxis)}[/] — {c.Descripcion}"))
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
