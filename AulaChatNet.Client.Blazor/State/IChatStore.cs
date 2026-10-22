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
    /// Reacciones por mensaje del canal actual (clave: Id del mensaje). El
    /// servidor manda el estado COMPLETO de cada mensaje, así que el
    /// componente solo REEMPLAZA la fila que cambia — o la borra si la
    /// lista llega vacía (alguien quitó la última reacción).
    /// </summary>
    IObservable<IReadOnlyDictionary<Guid, ReaccionesMensajeDto>> Reacciones { get; }

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

    /// <summary>
    /// Último valor del nick con el que estamos dados de alta (null hasta
    /// conectar). El procesador de comandos lo necesita para saber CUÁL de
    /// los mensajes de la vista es tuyo — /editar y /borrar actúan sobre tu
    /// último mensaje propio, igual que en la terminal.
    /// </summary>
    string? VistaNick { get; }

    /// <summary>Último valor de la lista de mensajes del canal actual (lo lee el comando /reaccion).</summary>
    IReadOnlyList<MensajeDto> VistaMensajes { get; }

    /// <summary>Último valor de las reacciones por mensaje del canal actual.</summary>
    IReadOnlyDictionary<Guid, ReaccionesMensajeDto> VistaReacciones { get; }

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
    /// Indica si puede quedar historial ANTERIOR al que ya se ve (para
    /// enseñar el botón de "cargar mensajes antiguos"). Empieza en true y
    /// se pone en false cuando el servidor responde una página vacía; al
    /// entrar o cambiar de canal vuelve a true.
    /// </summary>
    IObservable<bool> HayAnteriores { get; }

    /// <summary>
    /// Pide al servidor una página de mensajes ANTERIORES a los que ya se
    /// ven y la pone por delante en la vista (la web no repinta: recupera el
    /// scroll para que el lector no pierda su sitio). Es una invocación con
    /// retorno, así que el orden de llegada está garantizado.
    /// </summary>
    Task CargarAnterioresAsync();

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

    /// <summary>
    /// Alterna una reacción sobre un mensaje del canal actual (el mismo
    /// gesto quita lo que ya tenías). El eco vuelve por el evento de
    /// reacción actualizada: la vista se pinta SOLO desde ahí, igual que
    /// con los mensajes del canal.
    /// </summary>
    /// <param name="idMensaje">Mensaje a reaccionar (el de la fila elegida).</param>
    /// <param name="emoji">Emoji YA validado contra la lista cerrada por el llamador (el servidor vuelve a validar).</param>
    /// <returns>null si fue correcto; el error del servidor o de conexión si no.</returns>
    Task<string?> ReaccionarAsync(Guid idMensaje, string emoji);

    /// <summary>
    /// Edita el texto de UN mensaje propio del canal actual (Fase 4.4). El
    /// servidor valida que el mensaje sea tuyo y normal; el eco vuelve por
    /// el evento de mensaje editado (mensaje completo con la misma marca),
    /// y la vista se pinta SOLO desde ahí — igual que con los mensajes y
    /// las reacciones, la store no calcula nada.
    /// </summary>
    /// <param name="idMensaje">Mensaje a editar (el de la fila elegida).</param>
    /// <param name="texto">Nuevo texto ya recortado y validado en local.</param>
    /// <returns>null si fue correcto; el error del servidor o de conexión si no.</returns>
    Task<string?> EditarMensajeAsync(Guid idMensaje, string texto);

    /// <summary>
    /// Borra UN mensaje propio del canal actual (Fase 4.4), con las mismas
    /// reglas que editar. El eco vuelve por el evento de mensaje borrado
    /// (solo el Id): la store quita la fila de la vista y sus chips del
    /// mapa de reacciones. El borrado es definitivo.
    /// </summary>
    /// <param name="idMensaje">Mensaje a borrar (el de la fila elegida).</param>
    /// <returns>null si fue correcto; el error del servidor o de conexión si no.</returns>
    Task<string?> BorrarMensajeAsync(Guid idMensaje);

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

    /// <summary>
    /// Personas del canal que están escribiendo AHORA. Cada señal la
    /// renueva, y quien deja de señalar se descarta SOLO a los pocos
    /// segundos: no hace falta que el servidor avise de que se paró.
    /// </summary>
    IObservable<IReadOnlyList<string>> Escribiendo { get; }

    /// <summary>
    /// Avisa de que el usuario local está escribiendo. Cada tecla del input
    /// llama aquí; el muestreo —como mucho una señal por segundo— vive
    /// dentro de la store, así que una ráfaga de teclas no se convierte en
    /// una llamada al hub por pulsación.
    /// </summary>
    void SenalarEscritura();

    /// <summary>Última mención dirigida que ha llegado (null = ninguna esta sesión).</summary>
    IObservable<MencionDto?> UltimaMencion { get; }

    /// <summary>
    /// Menciones recibidas que aún no se han mirado. El contador es del
    /// CLIENTE —el servidor solo emite el aviso—, y lo vacía
    /// <see cref="MarcarMencionesVistas"/> al pulsar el badge.
    /// </summary>
    IObservable<int> MencionesNoVistas { get; }

    /// <summary>Marca las menciones como vistas: el badge vuelve a cero.</summary>
    void MarcarMencionesVistas();
}
