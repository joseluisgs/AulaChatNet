namespace AulaChatNet.Shared.Comandos;

/// <summary>
/// Convierte la línea escrita por el usuario en un <see cref="Comando"/>.
///
/// <remarks>
/// Es lógica PURA: sin UI, sin red, sin estado. Esa separación es la que
/// permite testear todos los casos (mayúsculas, sin almohadilla, argumento
/// ausente...) sin abrir una terminal ni montar un componente, que es
/// exactamente lo que hace <c>ParserComandosTests</c> (en Shared.Tests).
///
/// Vive en Shared porque los dos clientes usan MISMAS reglas: la línea que
/// la terminal entiende es la que entiende la web, y viceversa.
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

            // Los alias del privado NO se escriben "a pelo" aquí: salen de
            // Privados, la MISMA lista que usa EmpiezaPrivado (que la web
            // necesita para enseñar la nota mientras el usuario teclea)
            _ when EsNombrePrivado(nombre) => CrearPrivado(argumento),

            "/reaccion" or "/react" => CrearReaccion(argumento),
            "/editar" or "/edit" => CrearEditar(argumento),
            "/borrar" or "/delete" or "/del" => new Comando.Borrar(),
            "/reconectar" or "/reconnect" => new Comando.Reconectar(),
            "/sesion" => CrearSesion(argumento),
            "/limpiar" or "/clear" => new Comando.Limpiar(),
            "/salir" or "/exit" or "/quit" => new Comando.Salir(),
            _ => new Comando.Desconocido(partes[0])
        };
    }

    /// <summary>
    /// true si la línea va a ser un privado, aunque aún no lleve destino
    /// escrito. La web la usa para enseñar la nota de que no se guarda
    /// MIENTRAS el usuario teclea, sin duplicar los alias fuera de aquí.
    /// </summary>
    /// <param name="linea">Línea que se está escribiendo (puede ser null).</param>
    /// <returns>true si empieza por un alias de privado seguido de espacio o de nada.</returns>
    public static bool EmpiezaPrivado(string? linea)
    {
        var texto = (linea ?? string.Empty).TrimStart();

        foreach (var alias in Privados)
        {
            if (!texto.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                continue;

            // El alias debe ser una palabra COMPLETA: "/mpp hola" no es un
            // privado mal escrito, es un comando desconocido
            var despues = texto.Length == alias.Length ? ' ' : texto[alias.Length];

            return despues is ' ' or '\t';
        }

        return false;
    }

    /// <summary>
    /// A quién se le está escribiendo en la línea actual, AUNQUE todavía no
    /// lleve texto — algo que <see cref="Parse"/> devuelve como inválido. La
    /// web necesita ese nombre antes: es lo que pone en el chip "Privado a X"
    /// mientras el usuario sigue tecleando, y lo único que permite cerrarlo.
    /// </summary>
    /// <param name="linea">Línea que se está escribiendo (puede ser null).</param>
    /// <returns>El destinatario tecleado, o null si aún no hay ninguno.</returns>
    public static string? DestinatarioDePrivado(string? linea)
    {
        var texto = (linea ?? string.Empty).TrimStart();

        foreach (var alias in Privados)
        {
            if (!texto.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                continue;

            // Todo lo que va tras el alias, recortado: la primera palabra es
            // el destinatario, el resto será el mensaje (si llega)
            var resto = texto[alias.Length..].Trim();

            return resto.Length == 0
                ? null
                : resto.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        }

        return null;
    }

    /// <summary>Alias del comando de privado, en minúsculas: fuente única.</summary>
    private static readonly string[] Privados = ["/privado", "/mp", "/dm"];

    /// <summary>true si ese primer token (ya en minúsculas) es un comando de privado.</summary>
    private static bool EsNombrePrivado(string nombre) =>
        Privados.Contains(nombre, StringComparer.Ordinal);

    /// <summary>
    /// Construye el comando de privado: el argumento es "quien texto", así
    /// que se parte en DOS por el primer espacio. Si falta cualquiera de los
    /// dos no se manda nada a la red: devuelve el uso correcto para que el
    /// usuario lo aprenda sin mirar la ayuda entera.
    /// </summary>
    /// <param name="argumento">Texto escrito tras /privado (puede ser null).</param>
    /// <returns>Comando de privado o comando inválido con su uso.</returns>
    private static Comando CrearPrivado(string? argumento)
    {
        if (string.IsNullOrWhiteSpace(argumento))
            return new Comando.Invalido(
                "Falta el destinatario y el texto",
                "Usa: /privado <quien> <texto> (p. ej. /privado Ana ¿nos vemos?)");

        // Mismo corte que el de la línea entera: primer espacio separa al
        // destinatario del texto, que puede contener todos los que quiera
        var partes = argumento.Split(' ', 2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (partes.Length < 2)
            return new Comando.Invalido(
                "Falta el texto del mensaje privado",
                "Usa: /privado <quien> <texto> (p. ej. /privado Ana ¿nos vemos?)");

        return new Comando.Privado(partes[0], partes[1]);
    }

    /// <summary>
    /// Construye el comando de reacción: el argumento es SOLO el emoji, y se
    /// queda con la primera palabra por si alguien escribe "/reaccion 👍
    /// hola" — el resto se ignora en lugar de romper. Que el emoji esté en
    /// la lista cerrada lo comprueban después el cliente (para avisar ya) y
    /// el servidor (para no fiarse de nadie), no el parser: aquí solo se
    /// decide la FORMA de la línea.
    /// </summary>
    /// <param name="argumento">Texto escrito tras /reaccion (puede ser null).</param>
    /// <returns>Comando de reacción o comando inválido con su uso.</returns>
    private static Comando CrearReaccion(string? argumento)
    {
        if (string.IsNullOrWhiteSpace(argumento))
            return new Comando.Invalido(
                "Falta el emoji de la reacción",
                "Usa: /reaccion <emoji> (p. ej. /reaccion 👍)");

        var partes = argumento.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new Comando.Reaccion(partes[0]);
    }

    /// <summary>
    /// Construye el comando de edición: todo lo que va tras /editar es el
    /// NUEVO texto del mensaje, con todos sus espacios internos — solo se
    /// recortan los sobrantes de los extremos, igual que en un envío normal.
    /// El parser decide la forma; que el mensaje sea tuyo lo comprueba el
    /// servidor, que es quien tiene la verdad.
    /// </summary>
    /// <param name="argumento">Texto escrito tras /editar (puede ser null).</param>
    /// <returns>Comando de edición o comando inválido con su uso.</returns>
    private static Comando CrearEditar(string? argumento)
    {
        var texto = argumento?.Trim() ?? string.Empty;

        return texto.Length == 0
            ? new Comando.Invalido(
                "Falta el nuevo texto del mensaje",
                "Usa: /editar <texto> (p. ej. /editar Buenos días a todos)")
            : new Comando.Editar(texto);
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

    /// <summary>
    /// Construye el comando de sesión: sin argumento se ve lo guardado,
    /// "borrar" lo elimina (con sinónimos habituales); cualquier otra cosa
    /// devuelve el uso correcto en lugar de fallar en silencio.
    /// </summary>
    /// <param name="argumento">Acción escrita tras /sesion (puede ser null).</param>
    /// <returns>Comando de sesión o comando inválido con su uso.</returns>
    private static Comando CrearSesion(string? argumento) =>
        argumento?.ToLowerInvariant() switch
        {
            null or "" => new Comando.Sesion(AccionSesion.Ver),
            "borrar" or "eliminar" or "olvidar" => new Comando.Sesion(AccionSesion.Borrar),
            _ => new Comando.Invalido(
                $"No conozco la acción '{argumento}' de /sesion",
                "Usa: /sesion (ver lo guardado) o /sesion borrar")
        };
}
