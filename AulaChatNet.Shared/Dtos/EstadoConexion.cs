namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// Estado de la conexión con el servidor, visto desde el cliente.
/// Se actualiza con los eventos de reconexión de SignalR
/// (Reconnecting / Reconnected / Closed) y se expone como observable
/// para que la cabecera lo pinte con su punto de color.
/// </summary>
public enum EstadoConexion
{
    /// <summary>Sin conexión con el servidor.</summary>
    Desconectado,

    /// <summary>Conectando por primera vez.</summary>
    Conectando,

    /// <summary>Conectado y operativo.</summary>
    Conectado,

    /// <summary>Conexión caída; SignalR está reintentando solo.</summary>
    Reconectando
}
