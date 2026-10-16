using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Client.Blazor.State;

/// <summary>
/// Contrato de la store de chat del cliente web: la CONEXIÓN A SIGNALR
/// envuelta en estado reactivo, tal y como la consumen los componentes.
///
/// <remarks>
/// Patrón store (calco del <c>NotificationStore</c> de la Tienda y de la
/// <c>ClienteChat</c> de la terminal): los eventos de SignalR alimentan
/// observables internos; los componentes SOLO hablan con la store — nunca
/// entre ellos — y limpian sus suscripciones en <c>Dispose()</c>.
///
/// Regla de presentación: los errores de CAMPO se pintan junto al campo
/// (panel de entrada, caja de texto) y los errores de CONEXIÓN entran en el
/// flujo de mensajes con <see cref="Aviso"/>, como un mensaje de sistema.
/// </remarks>
/// </summary>
public interface IChatStore : IDisposable
{
    /// <summary>Vista del canal actual: historial reemplazado + mensajes en vivo.</summary>
    IObservable<IReadOnlyList<MensajeDto>> Mensajes { get; }

    /// <summary>Conectados del canal ACTUAL (evento que llega solo a tu grupo).</summary>
    IObservable<IReadOnlyList<UsuarioConectadoDto>> Usuarios { get; }

    /// <summary>Total de conectados y reparto por sala (evento GLOBAL: a todos).</summary>
    IObservable<EstadisticasDto> Estadisticas { get; }

    /// <summary>Canal canónico en el que estamos (null hasta conectar).</summary>
    IObservable<string?> CanalActual { get; }

    /// <summary>Nick con el que estamos dados de alta (null hasta conectar).</summary>
    IObservable<string?> Nick { get; }

    /// <summary>Estado de la conexión para el punto de la barra superior.</summary>
    IObservable<EstadoConexion> Estado { get; }

    /// <summary>
    /// true cuando hay una sesión iniciada (la pantalla está dentro del chat);
    /// false al llegar y al pulsar Salir. Es lo que decide qué mitad pinta la
    /// página: panel de entrada o vista de canal.
    /// </summary>
    IObservable<bool> EnSesion { get; }

    /// <summary>Canales disponibles, en el orden de la configuración.</summary>
    IReadOnlyList<string> Canales { get; }

    /// <summary>
    /// Abre la conexión y da de alta al usuario en un canal.
    /// </summary>
    /// <param name="usuario">Nombre ya recortado y validado por el llamador.</param>
    /// <param name="canal">Canal ya validado por el llamador.</param>
    /// <returns>
    /// <see cref="ResultadoConexion"/>: sin error si todo fue bien; con el
    /// mensaje traducido para el usuario y la distinción de si el servidor
    /// respondió (rechazo) o no se pudo alcanzar (fallo de red).
    /// </returns>
    Task<ResultadoConexion> ConectarAsync(string usuario, string canal);

    /// <summary>Difunde un texto al canal actual.</summary>
    /// <param name="texto">Texto ya recortado.</param>
    /// <returns>null si fue correcto; el error del servidor o de conexión si no.</returns>
    Task<string?> EnviarAsync(string texto);

    /// <summary>Cambia al canal dado (el historial nuevo llega después por evento).</summary>
    /// <param name="canal">Canal de destino.</param>
    /// <returns>null si el cambio fue correcto; el error, si no.</returns>
    Task<string?> CambiarCanalAsync(string canal);

    /// <summary>Cambia el nombre en caliente dentro del canal actual.</summary>
    /// <param name="nuevoNick">Nuevo nombre ya validado.</param>
    /// <returns>null si el cambio fue correcto; el error, si no.</returns>
    Task<string?> CambiarNickAsync(string nuevoNick);

    /// <summary>
    /// Reconexión manual para cuando la automática de SignalR se ha agotado:
    /// vuelve a arrancar el transporte y a dar de alta con la sesión actual.
    /// </summary>
    /// <returns>null si se volvió a entrar; el error, si no.</returns>
    Task<string?> ReconectarAsync();

    /// <summary>Cierra la conexión de forma ordenada y cierra la sesión de la UI.</summary>
    Task CerrarAsync();

    /// <summary>
    /// Aviso del cliente en la vista de mensajes (sin pasar por el servidor):
    /// las caídas y los fallos de conexión los conoce el cliente, y este aviso
    /// entra por el MISMO flujo que los mensajes del servidor.
    /// </summary>
    /// <param name="texto">Texto del aviso.</param>
    void Aviso(string texto);
}
