namespace AulaChatNet.Client.Terminal.Comandos;

/// <summary>
/// Convierte la línea escrita por el usuario en un <see cref="Comando"/>.
///
/// <remarks>
/// Es lógica PURA: sin UI, sin red, sin estado. Esa separación es la que
/// permite testear todos los casos (mayúsculas, sin almohadilla, argumento
/// ausente...) sin abrir una terminal, que es exactamente lo que hace
/// <c>ParserComandosTests</c>.
///
/// Reglas:
/// <list type="bullet">
/// <item>Lo que no empieza por "/" se envía al canal (<see cref="Comando.Enviar"/>).</item>
/// <item>"/" inicia un comando; mayúsculas y minúsculas dan igual (/CANAL vale).</item>
/// <item>El primer token es el comando; el resto, su argumento.</item>
/// </list>
/// Los nombres de comando son de Discord para que el alumno reconozca la
/// experiencia; los alias en inglés existen por si alguien viene de ahí.
/// </remarks>
/// </summary>
public static class ParserComandos
{
    /// <summary>
    /// Analiza una línea escrita por el usuario y devuelve el comando que representa.
    /// </summary>
    /// <param name="entrada">Línea cruda leída del prompt (puede ser null).</param>
    /// <returns>El comando tipado correspondiente; nunca null.</returns>
    public static Comando Parse(string? entrada)
    {
        var texto = entrada?.Trim() ?? string.Empty;

        // Sin texto o sin barra inicial: es un mensaje para el canal
        if (texto.Length == 0 || !texto.StartsWith('/'))
            return new Comando.Enviar(texto);

        // Primer token = nombre del comando; el resto = argumento (si lo hay)
        var partes = texto.Split(' ', 2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var nombre = partes[0].ToLowerInvariant();
        var argumento = partes.Length > 1 ? partes[1] : null;

        return nombre switch
        {
            "/ayuda" or "/help" => new Comando.Ayuda(argumento?.ToLowerInvariant()),
            "/canales" => new Comando.Canales(),
            "/canal" => CrearCanal(argumento),
            "/nick" or "/nickname" => CrearNick(argumento),
            "/usuarios" or "/quienes" => new Comando.Usuarios(argumento),
            "/limpiar" or "/clear" => new Comando.Limpiar(),
            "/salir" or "/exit" or "/quit" => new Comando.Salir(),
            _ => new Comando.Desconocido(partes[0])
        };
    }

    /// <summary>
    /// Construye el comando de cambio de canal; si falta el nombre, devuelve
    /// un <see cref="Comando.Invalido"/> con el uso correcto para que el
    /// usuario aprenda a escribirlo sin mirar la ayuda entera.
    /// </summary>
    /// <param name="argumento">Canal escrito tras /canal (puede ser null).</param>
    /// <returns>Comando de canal o comando inválido con su usage.</returns>
    private static Comando CrearCanal(string? argumento) =>
        string.IsNullOrWhiteSpace(argumento)
            ? new Comando.Invalido(
                "Falta el nombre del canal",
                "Usa: /canal <nombre> (p. ej. /canal daw)")
            : new Comando.Canal(argumento);

    /// <summary>
    /// Construye el comando de cambio de nick; si falta el nombre, devuelve
    /// un <see cref="Comando.Invalido"/> con el uso correcto.
    /// </summary>
    /// <param name="argumento">Nuevo nombre escrito tras /nick (puede ser null).</param>
    /// <returns>Comando de nick o comando inválido con su uso.</returns>
    private static Comando CrearNick(string? argumento) =>
        string.IsNullOrWhiteSpace(argumento)
            ? new Comando.Invalido(
                "Falta el nuevo nombre",
                "Usa: /nick <nombre> (p. ej. /nick Ana)")
            : new Comando.Nick(argumento);
}
