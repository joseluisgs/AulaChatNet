using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Spectre.Console;

namespace AulaChatNet.Client.Terminal.Consola;

/// <summary>
/// Fórmulas de markup para mostrar CUÁNTA GENTE hay conectada, en los dos
/// momentos en que se necesita: al ARRANCAR (antes de registrarse, con el
/// GET /estadisticas del servidor) y en la LÍNEA DE ESTADO de siempre
/// (con el canal actual marcado con el punto verde).
///
/// <remarks>
/// Está separada de Program.cs a propósito: al ser una clase estática PURA
/// —solo datos de entrada, cadena de salida— se testea sin red ni consola,
/// que es lo que hace <c>ConectadosTests</c>.
/// </remarks>
/// </summary>
public static class Conectados
{
    /// <summary>
    /// Mensaje de arranque: total de personas y reparto por sala, en el
    /// orden de la configuración.
    /// </summary>
    /// <param name="chat">Configuración compartida (da el orden de los canales).</param>
    /// <param name="est">Estadísticas que ha devuelto el servidor.</param>
    /// <returns>Cadena de markup lista para <c>AnsiConsole.MarkupLine</c>.</returns>
    public static string DescripcionArranque(ChatConfig chat, EstadisticasDto est)
    {
        var total = est.TotalConectados;

        if (total == 0)
            return "[grey]📌 Actualmente no hay nadie conectado todavía.[/]";

        // Singular y plural: "1 persona conectada" / "20 personas conectadas"
        var personas = total == 1
            ? "1 persona conectada"
            : $"{total} personas conectadas";

        return $"[grey]📌 Actualmente hay[/] [bold]{personas}[/][grey]:[/] " +
               ConteosPorSala(chat.Canales, est, canalActual: null);
    }

    /// <summary>
    /// Reparto por sala EN EL ORDEN DE LA CONFIGURACIÓN (el primero de la
    /// lista es el canal por defecto); si hay canal actual, lleva el punto
    /// verde de Discord. Las salas vacías salen con 0: el servidor solo
    /// emite las que tienen gente dentro.
    /// </summary>
    /// <param name="canales">Canales de la configuración, en orden.</param>
    /// <param name="est">Estadísticas del servidor.</param>
    /// <param name="canalActual">Canal en el que estamos (null = todavía ninguno).</param>
    /// <returns>Cadena de markup: <c>#general 12 · #daw 0 ● ...</c>.</returns>
    public static string ConteosPorSala(
        IReadOnlyList<string> canales, EstadisticasDto est, string? canalActual)
    {
        var porCanal = est.Canales
            .ToDictionary(c => c.Canal, c => c.Conectados, StringComparer.OrdinalIgnoreCase);

        var partes = new List<string>();

        foreach (var canal in canales)
        {
            porCanal.TryGetValue(canal, out var conectados);
            var nombre = Markup.Escape(canal);

            var esActual = canalActual is not null
                && string.Equals(canal, canalActual, StringComparison.OrdinalIgnoreCase);

            // SIN anidar estilos: cada pieza lleva su etiqueta completa y
            // cerrada (un ] suelto o un cierre de más rompen el repintado)
            partes.Add(esActual
                ? $"[green]{nombre} {conectados} ●[/]"
                : $"{nombre} [white]{conectados}[/]");
        }

        return string.Join("[grey] · [/]", partes);
    }
}
