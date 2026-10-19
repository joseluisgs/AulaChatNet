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

    /// <summary>
    /// Mensajes privados recibidos y enviados: solo SE AÑADEN (nunca se
    /// reemplazan, porque el servidor no guarda ninguno). Vive aparte de
    /// <see cref="Mensajes"/> para que el historial del canal no pueda
    /// contenerlos, ni hoy ni al cargarlo del disco.
    /// </summary>
    IObservable<IReadOnlyList<MensajePrivadoDto>> Privados { get; }

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

    /// <summary>
    /// Persona elegida en la lista de conectados para escribirle un privado
    /// (null = no se está preparando ninguno). No es estado de SignalR sino
    /// de la UI, y vive aquí por lo mismo que <see cref="EnSesion"/>: es lo
    /// que comparten los componentes, y los componentes no se hablan entre
    /// ellos — hablan con la store.
    /// </summary>
    IObservable<string?> DestinatarioPrivado { get; }

    /// <summary>Canales disponibles, en el orden de la configuración.</summary>
    IReadOnlyList<string> Canales { get; }

    // ── Valores actuales (calco de ClienteChat: el procesador de comandos
    // y la UI necesitan el valor YA, no un evento futuro) ──

    /// <summary>Último valor del canal canónico en el que estamos (null hasta conectar).</summary>
    string? Canal { get; }

    /// <summary>Último valor de la lista de conectados del canal actual.</summary>
    IReadOnlyList<UsuarioConectadoDto> VistaUsuarios { get; }

    /// <summary>Último valor de la lista de privados de ESTA sesión.</summary>
    IReadOnlyList<MensajePrivadoDto> VistaPrivados { get; }

    /// <summary>Último valor de las estadísticas globales (total y por sala).</summary>
    EstadisticasDto EstadisticasActuales { get; }

    /// <summary>Último valor del estado de la conexión.</summary>
    EstadoConexion EstadoValor { get; }

    /// <summary>
    /// Consulta los conectados de CUALQUIER canal: el servidor responde SOLO
    /// a este cliente (no difunde nada), igual que un GET de una API.
    /// </summary>
    /// <param name="canal">Canal a consultar (ya validado por el llamador).</param>
    /// <returns>La lista de esa sala, o null si el servidor no conoce el canal o no hay conexión.</returns>
    Task<IReadOnlyList<UsuarioConectadoDto>?> ObtenerUsuariosAsync(string canal);

    /// <summary>
    /// Vacía la vista local de mensajes (comando /limpiar): solo es una
    /// limpieza de PANTALLA — el historial vive en el servidor y vuelve al
    /// cambiar de canal o al reconectar.
    /// </summary>
    void LimpiarVista();

    /// <summary>
    /// Pide escribir un privado a esa persona: la caja de texto se prellena
    /// con el comando y muestra la nota de que no se guarda. El clic viene
    /// de la lista de conectados, que no puede tocar la caja directamente.
    /// </summary>
    /// <param name="destinatario">Nombre de la persona elegida (ya validado por el llamador).</param>
    void PrepararPrivado(string destinatario);

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

    /// <summary>
    /// Envía un mensaje a UNA persona del canal actual. El eco vuelve por el
    /// evento de privado recibido: la vista se pinta SOLO desde ahí, igual
    /// que con los mensajes del canal.
    /// </summary>
    /// <param name="destinatario">Nombre de quien lo recibe (validado por el llamador).</param>
    /// <param name="texto">Texto ya recortado.</param>
    /// <returns>null si fue correcto; el error del servidor o de conexión si no.</returns>
    Task<string?> EnviarPrivadoAsync(string destinatario, string texto);

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
