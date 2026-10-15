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
}
