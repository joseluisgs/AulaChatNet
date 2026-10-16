using Spectre.Console;

namespace AulaChatNet.Client.Terminal.Consola;

/// <summary>
/// Pantalla de entrada del chat: el banner ASCII grande en blurple,
/// como la cabecera de una app de consola que se precie.
/// </summary>
public static class Banner
{
    /// <summary>
    /// Muestra el banner y el subtítulo del proyecto.
    /// "AulaChat" (y no "AulaChatNet") a propósito: el fuente ASCII de
    /// Spectre ocupa ~11 columnas por letra, y con 11 letras no cabría en
    /// una terminal de 80 columnas.
    /// </summary>
    public static void Mostrar()
    {
        AnsiConsole.Write(
            new FigletText("AulaChat")
                .LeftJustified()
                .Color(TemaDiscord.Blurple));

        AnsiConsole.MarkupLine(
            "[grey]Chat en tiempo real para el aula · .NET 10 + SignalR + Rx[/]");
        AnsiConsole.WriteLine();
    }
}
