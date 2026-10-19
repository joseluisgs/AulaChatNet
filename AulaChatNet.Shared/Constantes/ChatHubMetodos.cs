namespace AulaChatNet.Shared.Constantes;

/// <summary>
/// Nombres de los métodos públicos del ChatHub.
/// Los clientes no escriben los nombres "a pelo": usan estas constantes
/// para que un cambio de nombre en el servidor no rompa los clientes en silencio.
/// </summary>
public static class ChatHubMetodos
{
    /// <summary>Método para darse de alta al conectar: ConnectAsync(usuario).</summary>
    public const string Conectar = "ConnectAsync";

    /// <summary>Método para enviar un mensaje al canal: SendAsync(texto).</summary>
    public const string Enviar = "SendAsync";

    /// <summary>Método para moverse a otro canal: CambiarCanalAsync(canal).</summary>
    public const string CambiarCanal = "CambiarCanalAsync";

    /// <summary>Método para cambiar el nombre en caliente: CambiarNickAsync(nuevoNick).</summary>
    public const string CambiarNick = "CambiarNickAsync";

    /// <summary>
    /// Método de CONSULTA (no difunde): devuelve los conectados de un canal
    /// cualquiera solo a quien lo pregunta: ObtenerUsuariosAsync(canal).
    /// </summary>
    public const string ObtenerUsuarios = "ObtenerUsuariosAsync";

    /// <summary>
    /// Método para enviar un mensaje a UNA persona concreta de tu canal:
    /// EnviarPrivadoAsync(destinatario, texto). NO se difunde al grupo.
    /// </summary>
    public const string EnviarPrivado = "EnviarPrivadoAsync";

    /// <summary>
    /// Método para avisar de que estás escribiendo: EscribiendoAsync().
    /// Sin argumentos y SIN texto: el servidor solo reenvía tu nombre al
    /// resto de tu canal, que es quien decide cuánto dura en pantalla.
    /// </summary>
    public const string Escribiendo = "EscribiendoAsync";

    /// <summary>
    /// Método para reaccionar a un mensaje: ReaccionarAsync(idMensaje, emoji).
    /// El servidor ALTERNA (si ya tenías ese emoji, te lo quita), valida que
    /// el emoji esté en la lista cerrada de Shared y difunde el estado del
    /// mensaje a todo el canal — también a ti, para que veas tu propio chip.
    /// </summary>
    public const string Reaccionar = "ReaccionarAsync";
}
