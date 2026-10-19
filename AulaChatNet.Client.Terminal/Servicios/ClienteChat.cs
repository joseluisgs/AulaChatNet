using System.Reactive.Subjects;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Microsoft.AspNetCore.SignalR.Client;

namespace AulaChatNet.Client.Terminal.Servicios;

/// <summary>
/// Conexión al ChatHub envuelta en una capa reactiva (patrón store estilo
/// NotificationStore de Blazor): los eventos de SignalR alimentan
/// <c>BehaviorSubject</c> que los clientes consumen como <c>IObservable</c>.
///
/// <remarks>
/// Estado reactivo que expone:
/// <list type="bullet">
/// <item><see cref="Mensajes"/> → la vista del canal actual: se REEMPLAZA con
/// el historial al entrar/cambiar de canal y se AÑADE con cada MensajeNuevo.</item>
    /// <item><see cref="Privados"/>: los mensajes privados, que solo SE AÑADEN
    /// y nunca se reemplazan, porque el servidor no guarda ninguno.</item>
/// <item><see cref="Usuarios"/> → conectados del canal actual.</item>
/// <item><see cref="Estadisticas"/> → total de conectados y reparto por SALA
/// (evento global: la línea de estado sabe cuánta gente hay y dónde).</item>
/// <item><see cref="CanalActual"/> → canal canónico en el que estamos.</item>
/// <item><see cref="Estado"/> → punto de la cabecera (conectado/reconectando/...).</item>
/// </list>
///
/// No se usan bloqueos: los eventos de SignalR llegan SERIALIZADOS por
/// conexión (una invocación detrás de otra), por lo que los OnNext son
/// secuenciales. Documentarlo es parte de lo que se enseña aquí.
///
/// Los nombres de evento NO se escriben "a pelo": vienen de
/// <see cref="ChatHubEventos"/>, la misma fuente que usa el servidor.
/// </remarks>
/// </summary>
public class ClienteChat : IDisposable
{
    private readonly HubConnection _conexion;
    private readonly ChatConfig _config;

    private readonly BehaviorSubject<IReadOnlyList<MensajeDto>> _mensajes = new([]);
    private readonly BehaviorSubject<IReadOnlyList<MensajePrivadoDto>> _privados = new([]);
    private readonly BehaviorSubject<IReadOnlyList<UsuarioConectadoDto>> _usuarios = new([]);
    private readonly BehaviorSubject<EstadisticasDto> _estadisticas = new(new EstadisticasDto(0, []));
    private readonly BehaviorSubject<string?> _canalActual = new(null);
    private readonly BehaviorSubject<EstadoConexion> _estado = new(EstadoConexion.Desconectado);

    // Último nick y canal con los que se logró registrar: la reconexión los
    // reutiliza para volver a darse de alta sin volver a preguntar al usuario
    private string? _usuarioActual;

    // true mientras la conexión se cierra A PROPÓSITO (salida ordenada o
    // registro rechazado): el evento Closed salta también en esos casos y
    // no queremos un aviso de "conexión perdida" cuando la perdimos nosotros.
    // Ojo: SignalR invoca Closed de forma ASÍNCRONA, a veces DESPUÉS de que
    // StopAsync devuelva, así que el flag NO se resetea al cerrar — solo lo
    // hace ConectarAsync/ReconectarAsync al empezar la siguiente conexión.
    // Si no, el cierre tardío se encontraría el flag ya en false (aviso
    // fantasma justo después de un rechazo de nick).
    private bool _cierreIntencionado;

    /// <summary>Error devuelto al invocar el hub con la conexión caída.</summary>
    private const string ErrorSinConexion = "Sin conexión con el servidor";

    /// <summary>
    /// Crea el cliente y prepara la conexión (aún NO conectada: eso es
    /// <see cref="ConectarAsync"/>).
    /// </summary>
    /// <param name="urlHub">URL completa del hub (p. ej. http://localhost:5030/hubs/chat).</param>
    /// <param name="config">ChatConfig compartido (canales y límites).</param>
    public ClienteChat(string urlHub, ChatConfig config)
    {
        _config = config;

        _conexion = new HubConnectionBuilder()
            .WithUrl(urlHub)
            // Reconexión automática de SignalR: reintenta a los 0, 2, 10 y
            // 30 segundos; si se rinde, se dispara Closed y lo notificamos
            .WithAutomaticReconnect()
            .Build();

        // ── Eventos del servidor → estado reactivo local ──
        _conexion.On<MensajeDto>(ChatHubEventos.MensajeNuevo, mensaje =>
        {
            // La vista del canal = historial recibido + mensajes que llegan
            _mensajes.OnNext([.. _mensajes.Value, mensaje]);
        });

        // Privados: SOLO llegan a los dos implicados (el servidor los emite a
        // dos conexiones concretas, nunca al grupo). Se añaden a SU propia
        // lista y no a la del canal, de modo que el historial de la sala
        // jamás los contiene — ni hoy ni cuando se cargue del disco.
        _conexion.On<MensajePrivadoDto>(ChatHubEventos.PrivadoRecibido, privado =>
        {
            _privados.OnNext([.. _privados.Value, privado]);
        });

        _conexion.On<IReadOnlyList<UsuarioConectadoDto>>(
            ChatHubEventos.ListaUsuariosActualizada, lista => _usuarios.OnNext(lista));

        // Estadísticas GLOBALES (total y por sala): llegan a todos los clientes
        // cada vez que alguien entra, sale o cambia de canal
        _conexion.On<EstadisticasDto>(
            ChatHubEventos.EstadisticasActualizadas, est => _estadisticas.OnNext(est));

        _conexion.On<IReadOnlyList<MensajeDto>>(ChatHubEventos.HistorialRecibido, historial =>
        {
            // El historial REEMPLAZA la vista: así no se mezclan canales
            _mensajes.OnNext(historial);
        });

        // ── Ciclo de vida de la conexión → punto de estado de la cabecera ──
        _conexion.Reconnecting += _ =>
        {
            _estado.OnNext(EstadoConexion.Reconectando);
            return Task.CompletedTask;
        };

        // Reconexión automática de SignalR: el transporte vuelve solo, pero
        // el ALTA en el servidor no (ver ReenviarAltaAsync)
        _conexion.Reconnected += _ => ReenviarAltaAsync();

        _conexion.Closed += _ =>
        {
            _estado.OnNext(EstadoConexion.Desconectado);

            // Aviso solo cuando la caída NO la pedimos nosotros: así el
            // modo tubería (que no tiene cabecera) también se entera
            if (!_cierreIntencionado)
                AvisoLocal("Se perdió la conexión con el servidor.");

            return Task.CompletedTask;
        };
    }

    // ── Observables (para suscribirse) ──

    /// <summary>Vista actual del canal: historial + mensajes en vivo.</summary>
    public IObservable<IReadOnlyList<MensajeDto>> Mensajes => _mensajes;

    /// <summary>
    /// Privados recibidos y enviados: viven SOLO en este cliente, porque el
    /// servidor no los guarda. Por eso no hay evento de "historial de
    /// privados": lo que ves es todo lo que hay.
    /// </summary>
    public IObservable<IReadOnlyList<MensajePrivadoDto>> Privados => _privados;

    /// <summary>Conectados del canal actual.</summary>
    public IObservable<IReadOnlyList<UsuarioConectadoDto>> Usuarios => _usuarios;

    /// <summary>Total de conectados y reparto por sala (evento global).</summary>
    public IObservable<EstadisticasDto> Estadisticas => _estadisticas;

    /// <summary>Canal canónico en el que estamos (null hasta conectar).</summary>
    public IObservable<string?> CanalActual => _canalActual;

    /// <summary>Estado de la conexión para pintar el punto de la cabecera.</summary>
    public IObservable<EstadoConexion> Estado => _estado;

    // ── Valores actuales (el bucle de la TUI los lee sin suscribirse) ──

    /// <summary>Último valor de la vista de mensajes (BehaviorSubject lo guarda).</summary>
    public IReadOnlyList<MensajeDto> VistaMensajes => _mensajes.Value;

    /// <summary>Último valor de la vista de privados.</summary>
    public IReadOnlyList<MensajePrivadoDto> VistaPrivados => _privados.Value;

    /// <summary>Último valor de la lista de conectados.</summary>
    public IReadOnlyList<UsuarioConectadoDto> VistaUsuarios => _usuarios.Value;

    /// <summary>Último valor de las estadísticas (total y por sala).</summary>
    public EstadisticasDto EstadisticasActuales => _estadisticas.Value;

    /// <summary>Último valor del canal actual.</summary>
    public string? Canal => _canalActual.Value;

    /// <summary>Último valor del estado de conexión.</summary>
    public EstadoConexion EstadoValor => _estado.Value;

    /// <summary>Canales disponibles (de la configuración compartida).</summary>
    public IReadOnlyList<string> Canales => _config.Canales;

    // ── Métodos (los mismos que expone el hub) ──

    /// <summary>
    /// Abre la conexión y da de alta al usuario en un canal.
    /// </summary>
    /// <param name="usuario">Nombre (ya validado localmente por el llamador).</param>
    /// <param name="canal">Canal (ya validado localmente por el llamador).</param>
    /// <returns>
    /// <see cref="ResultadoConexion"/>: sin error si todo fue bien; con el error
    /// traducido para el usuario y la distinción de si el servidor respondió
    /// (rechazo) o ni se pudo alcanzar (fallo de red) — de ahí depende que se
    /// repregunte el nick o no.
    /// </returns>
    public async Task<ResultadoConexion> ConectarAsync(string usuario, string canal)
    {
        try
        {
            // Aquí se "despierta" el flag de cierre intencionado: el Closed
            // tardío de un intento anterior (rechazo o salida) ya no debe
            // confundirse con una caída inesperada de ESTA conexión
            _cierreIntencionado = false;
            _estado.OnNext(EstadoConexion.Conectando);
            await _conexion.StartAsync();
        }
        catch (Exception ex)
        {
            // No hay servidor, hay un firewall... el ALTA ni se intenta, y el
            // mensaje se traduce a palabras de usuario (nunca "HttpRequestException")
            _estado.OnNext(EstadoConexion.Desconectado);

            return new ResultadoConexion(
                $"No se pudo conectar con el servidor: {MensajesError.DescripcionDe(ex)}",
                HayServidor: false);
        }

        var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.Conectar, usuario, canal);

        if (error is not null)
        {
            // El servidor respondió y rechazó el alta (p. ej. nombre en uso):
            // cerramos para que el llamador pueda preguntar de nuevo sin
            // estados colgados. El cierre es NUESTRO, así que Closed no
            // avisa — y como ese evento puede llegar DESPUÉS de que StopAsync
            // devuelva, el flag NO se resetea aquí (lo hará la próxima
            // llamada a ConectarAsync): así no hay aviso fantasma.
            _estado.OnNext(EstadoConexion.Desconectado);
            _cierreIntencionado = true;
            await _conexion.StopAsync();

            return new ResultadoConexion(error, HayServidor: true);
        }

        // Identidad de cara a la reconexión: con estos dos valores basta
        // para volver a darse de alta si la conexión se cae (ver ReenviarAlta)
        _usuarioActual = usuario;
        _canalActual.OnNext(_config.EncontrarCanal(canal));
        _estado.OnNext(EstadoConexion.Conectado);

        return new ResultadoConexion(null, HayServidor: true);
    }

    /// <summary>
    /// Difunde un mensaje al canal actual.
    /// </summary>
    /// <param name="texto">Texto ya recortado.</param>
    /// <returns>null si todo fue correcto; el error del servidor si no.</returns>
    public async Task<string?> EnviarAsync(string texto)
    {
        try
        {
            return await _conexion.InvokeAsync<string?>(ChatHubMetodos.Enviar, texto);
        }
        catch (Exception)
        {
            // Conexión caída: InvokeAsync LANZA en vez de devolver el error
            // del servidor, así que lo convertimos al mismo patrón string?
            return ErrorSinConexion;
        }
    }

    /// <summary>
    /// Envía un mensaje privado a UNA persona del canal actual. El eco del
    /// propio mensaje vuelve por el evento <c>PrivadoRecibido</c>: la vista
    /// se pinta SOLO desde ahí, igual que con los mensajes del canal, para
    /// que los dos casos sigan el mismo camino.
    /// </summary>
    /// <param name="destinatario">Nombre de quien lo recibe (validado por el llamador).</param>
    /// <param name="texto">Texto ya recortado.</param>
    /// <returns>null si todo fue correcto; el error del servidor o de conexión si no.</returns>
    public async Task<string?> EnviarPrivadoAsync(string destinatario, string texto)
    {
        try
        {
            return await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.EnviarPrivado, destinatario, texto);
        }
        catch (Exception)
        {
            // Mismo traductor que EnviarAsync: sin conexión, error en español
            return ErrorSinConexion;
        }
    }

    /// <summary>
    /// Cambia al canal dado; el historial del nuevo canal llegará después
    /// por el evento HistorialRecibido y reemplazará la vista.
    /// </summary>
    /// <param name="canal">Canal de destino (se valida localmente antes).</param>
    /// <returns>null si el cambio fue correcto; el error del servidor si no.</returns>
    public async Task<string?> CambiarCanalAsync(string canal)
    {
        try
        {
            var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.CambiarCanal, canal);

            if (error is null)
                _canalActual.OnNext(_config.EncontrarCanal(canal));

            return error;
        }
        catch (Exception)
        {
            return ErrorSinConexion;
        }
    }

    /// <summary>
    /// Cambia el nombre en caliente dentro del canal actual.
    /// </summary>
    /// <param name="nuevoNick">Nuevo nombre (ya validado localmente).</param>
    /// <returns>null si el cambio fue correcto; el error del servidor si no.</returns>
    public async Task<string?> CambiarNickAsync(string nuevoNick)
    {
        try
        {
            var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.CambiarNick, nuevoNick);

            if (error is null)
                _usuarioActual = nuevoNick; // la reconexión usará el nick nuevo

            return error;
        }
        catch (Exception)
        {
            return ErrorSinConexion;
        }
    }

    /// <summary>
    /// Consulta los conectados de CUALQUIER canal: el servidor responde SOLO
    /// a este cliente (no difunde nada), igual que un GET de una API.
    /// </summary>
    /// <param name="canal">Canal a consultar (ya validado por el llamador).</param>
    /// <returns>La lista de esa sala, o null si el servidor no conoce el canal o no hay conexión.</returns>
    public async Task<IReadOnlyList<UsuarioConectadoDto>?> ObtenerUsuariosAsync(string canal)
    {
        try
        {
            return await _conexion.InvokeAsync<IReadOnlyList<UsuarioConectadoDto>?>(
                ChatHubMetodos.ObtenerUsuarios, canal);
        }
        catch (Exception)
        {
            // null también significa "no pude consultar": el que lo pinta
            // usa un mensaje que vale para ambos casos
            return null;
        }
    }

    /// <summary>
    /// Tras una reconexión automática de SignalR: el transporte vuelve SOLO,
    /// pero el ALTA no. Al caer, el servidor ya ejecutó OnDisconnectedAsync
    /// (nos dio de baja y nos quitó del grupo del canal), y la conexión
    /// nueva tiene OTRO ConnectionId que no está en ningún grupo. Sin este
    /// paso, el cliente quedaría de escayola: la cabecera dice "conectado"
    /// pero nadie nos ve y a nosotros no nos llega nada.
    /// </summary>
    private async Task ReenviarAltaAsync()
    {
        _estado.OnNext(EstadoConexion.Conectado);

        // Aún no hay identidad (la conexión se cayó antes del primer alta):
        // no hay nada que re-registrar
        if (_usuarioActual is null || _canalActual.Value is null)
            return;

        try
        {
            var error = await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.Conectar, _usuarioActual, _canalActual.Value);

            // El servidor re-registra y RE-ENVÍA historial, lista y
            // estadísticas; este aviso solo confirma el alta local
            AvisoLocal(error is null
                ? $"Reconectado: vuelto a entrar en {_canalActual.Value} como {_usuarioActual}."
                : $"No pude volver a registrar la sesión: {error}");
        }
        catch (Exception)
        {
            // Se cayó otra vez mientras re-registrábamos: el bucle de la
            // conexión ya se encarga de reconectar (Reconnecting/Reconnected)
        }
    }

    /// <summary>
    /// Reconexión MANUAL para cuando la automática se ha agotado (Closed):
    /// vuelve a arrancar el transporte y a dar de alta al usuario con el
    /// nick y el canal de la sesión actual, sin reiniciar el programa.
    /// </summary>
    /// <returns>null si se volvió a entrar; el error, si no.</returns>
    public async Task<string?> ReconectarAsync()
    {
        if (_usuarioActual is null || _canalActual.Value is null)
            return "No hay ninguna sesión previa para reconectar";

        try
        {
            _cierreIntencionado = false;
            _estado.OnNext(EstadoConexion.Conectando);
            await _conexion.StartAsync();

            var error = await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.Conectar, _usuarioActual, _canalActual.Value);

            if (error is not null)
            {
                _estado.OnNext(EstadoConexion.Desconectado);
                return error;
            }

            _estado.OnNext(EstadoConexion.Conectado);
            return null;
        }
        catch (Exception ex)
        {
            _estado.OnNext(EstadoConexion.Desconectado);

            // Mismo traductor: en pantalla, palabras de usuario — jamás el
            // tipo de la excepción
            return $"No se pudo reconectar: {MensajesError.DescripcionDe(ex)}";
        }
    }

    /// <summary>
    /// Aviso local en la vista de mensajes (sin pasar por el servidor): los
    /// cambios de conexión los conoce el cliente, y la UI solo pinta lo que
    /// hay en los stores — este también entra por ahí, como un mensaje de
    /// sistema más, para que los dos modos de entrada lo vean igual.
    /// </summary>
    /// <param name="texto">Texto del aviso.</param>
    private void AvisoLocal(string texto)
    {
        var aviso = new MensajeDto(
            Guid.NewGuid(), MensajesSistema.Autor, texto, DateTime.UtcNow, TipoMensaje.Sistema);

        _mensajes.OnNext([.. _mensajes.Value, aviso]);
    }

    /// <summary>
    /// Cierra la conexión de forma ordenada (el servidor despide al usuario
    /// en su canal con OnDisconnectedAsync).
    /// </summary>
    public async Task CerrarAsync()
    {
        try
        {
            // Cierre pedido: Closed no avisa. El flag NO se resetea al
            // terminar (SignalR puede entregar Closed tardío); lo hará la
            // próxima ConectarAsync, al empezar la siguiente conexión.
            _cierreIntencionado = true;
            await _conexion.StopAsync();
            _estado.OnNext(EstadoConexion.Desconectado);
        }
        catch
        {
            // La conexión ya estaba caída: nada que cerrar, y una salida
            // jamás debe lanzar excepción
        }
    }

    /// <summary>Libera los observables y la conexión SignalR.</summary>
    public void Dispose()
    {
        _mensajes.Dispose();
        _privados.Dispose();
        _usuarios.Dispose();
        _estadisticas.Dispose();
        _canalActual.Dispose();
        _estado.Dispose();

        // DisposeAsync en segundo plano: estamos saliendo y no queremos
        // bloquear el hilo principal en el Dispose
        _ = _conexion.DisposeAsync();
    }
}
