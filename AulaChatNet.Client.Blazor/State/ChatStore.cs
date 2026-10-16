using System.Reactive.Subjects;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AulaChatNet.Client.Blazor.State;

/// <inheritdoc cref="IChatStore" />
///
/// <remarks>
/// Es el PUERTO de la <c>ClienteChat</c> de la terminal: mismo contrato con
/// el hub, mismos estados y mismas traducciones de error; aquí lo único que
/// se añade es <see cref="EnSesion"/> (para que la página sepa qué mitad
/// pintar) y el nick como observable (para la barra superior).
///
/// No se usan bloqueos: los eventos de SignalR llegan SERIALIZADOS por
/// conexión (una invocación detrás de otra), así que los OnNext son
/// secuenciales. Los nombres de evento NO se escriben "a pelo": vienen de
/// <see cref="ChatHubEventos"/>, la misma fuente que usa el servidor.
/// </remarks>
public class ChatStore : IChatStore
{
    private readonly HubConnection _conexion;
    private readonly ChatConfig _config;

    // URL del hub y logger: dos detalles que solo interesan para DIAGNÓSTICO
    // (¿a dónde intentaba conectarse? ¿qué falló?) — en pantalla nunca salen,
    // en la consola del navegador sí
    private readonly string _urlHub;
    private readonly ILogger<ChatStore> _log;

    private readonly BehaviorSubject<IReadOnlyList<MensajeDto>> _mensajes = new([]);
    private readonly BehaviorSubject<IReadOnlyList<UsuarioConectadoDto>> _usuarios = new([]);
    private readonly BehaviorSubject<EstadisticasDto> _estadisticas = new(new EstadisticasDto(0, []));
    private readonly BehaviorSubject<string?> _canalActual = new(null);
    private readonly BehaviorSubject<string?> _nick = new(null);
    private readonly BehaviorSubject<EstadoConexion> _estado = new(EstadoConexion.Desconectado);
    private readonly BehaviorSubject<bool> _enSesion = new(false);

    // Último nick y canal con los que se logró registrar: la reconexión los
    // reutiliza para volver a darse de alta sin volver a preguntar al usuario
    private string? _usuarioActual;

    // true mientras CERRAMOS la conexión a propósito (salida ordenada con el
    // botón Salir o registro rechazado): el evento Closed salta también en
    // esos casos y no queremos un aviso de "conexión perdida" cuando la
    // perdimos nosotros
    private bool _cierreIntencionado;

    /// <summary>Error devuelto al invocar el hub con la conexión caída.</summary>
    private const string ErrorSinConexion = "Sin conexión con el servidor";

    /// <summary>
    /// Crea la store y prepara la conexión (aún NO conectada: eso es
    /// <see cref="ConectarAsync"/>). La URL la aporta Program.cs al registrar
    /// el servicio: base del servidor (mismo origen por defecto) + HubRuta.
    /// </summary>
    /// <param name="urlHub">URL completa del hub (p. ej. http://localhost:5030/hubs/chat).</param>
    /// <param name="config">ChatConfig compartido (canales y límites).</param>
    /// <param name="logger">
    /// Logger opcional (la app registra Serilog → consola del navegador). Si
    /// no llega (tests que la construyen sin DI), se usa NullLogger: el chat
    /// nunca debe dejar de arranque por un logger.
    /// </param>
    public ChatStore(string urlHub, ChatConfig config, ILogger<ChatStore>? logger = null)
    {
        _config = config;
        _urlHub = urlHub;
        _log = logger ?? NullLogger<ChatStore>.Instance;

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

        _conexion.On<IReadOnlyList<UsuarioConectadoDto>>(
            ChatHubEventos.ListaUsuariosActualizada, lista => _usuarios.OnNext(lista));

        // Estadísticas GLOBALES (total y por sala): llegan a todos los clientes
        // cada vez que alguien entra, sale o cambia de canal. Mismo evento que
        // la línea de estado de la terminal y que las píldoras de canal.
        _conexion.On<EstadisticasDto>(
            ChatHubEventos.EstadisticasActualizadas, est => _estadisticas.OnNext(est));

        _conexion.On<IReadOnlyList<MensajeDto>>(ChatHubEventos.HistorialRecibido, historial =>
        {
            // El historial REEMPLAZA la vista: así no se mezclan canales
            _mensajes.OnNext(historial);
        });

        // ── Ciclo de vida de la conexión → punto de la barra ──
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

            // Aviso solo cuando la caída NO la pedimos nosotros: así un
            // cierre ordenado (Salir) no suelta un aviso fantasma
            if (!_cierreIntencionado)
            {
                Aviso("Se perdió la conexión con el servidor.");
                _log.LogWarning("Conexión perdida de forma inesperada con {UrlHub}", _urlHub);
            }

            return Task.CompletedTask;
        };
    }

    /// <inheritdoc />
    public IObservable<IReadOnlyList<MensajeDto>> Mensajes => _mensajes;

    /// <inheritdoc />
    public IObservable<IReadOnlyList<UsuarioConectadoDto>> Usuarios => _usuarios;

    /// <inheritdoc />
    public IObservable<EstadisticasDto> Estadisticas => _estadisticas;

    /// <inheritdoc />
    public IObservable<string?> CanalActual => _canalActual;

    /// <inheritdoc />
    public IObservable<string?> Nick => _nick;

    /// <inheritdoc />
    public IObservable<EstadoConexion> Estado => _estado;

    /// <inheritdoc />
    public IObservable<bool> EnSesion => _enSesion;

    /// <inheritdoc />
    public IReadOnlyList<string> Canales => _config.Canales;

    /// <inheritdoc />
    public string? Canal => _canalActual.Value;

    /// <inheritdoc />
    public IReadOnlyList<UsuarioConectadoDto> VistaUsuarios => _usuarios.Value;

    /// <inheritdoc />
    public EstadisticasDto EstadisticasActuales => _estadisticas.Value;

    /// <inheritdoc />
    public EstadoConexion EstadoValor => _estado.Value;

    /// <inheritdoc />
    public async Task<ResultadoConexion> ConectarAsync(string usuario, string canal)
    {
        try
        {
            _cierreIntencionado = false;
            _estado.OnNext(EstadoConexion.Conectando);
            await _conexion.StartAsync();
        }
        catch (Exception ex)
        {
            // No hay servidor, hay un firewall... el ALTA ni se intenta, y el
            // mensaje se traduce a palabras de usuario (nunca "HttpRequestException")
            _estado.OnNext(EstadoConexion.Desconectado);

            // En pantalla, palabras de usuario; en el log, el detalle completo
            // (excepción y URL intentada) para poder diagnosticar
            _log.LogError(ex, "No se pudo conectar con {UrlHub}: {Motivo}", _urlHub, ex.Message);

            return new ResultadoConexion(
                $"No se pudo conectar con el servidor: {MensajesError.DescripcionDe(ex)}",
                HayServidor: false);
        }

        var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.Conectar, usuario, canal);

        if (error is not null)
        {
            // El servidor respondió y rechazó el alta (p. ej. nombre en uso):
            // cerramos para no dejar estados colgados. El cierre es NUESTRO,
            // así que Closed no lanza ningún aviso
            _log.LogWarning("El hub rechazó a {Usuario} en {Canal}: {Error}", usuario, canal, error);
            _estado.OnNext(EstadoConexion.Desconectado);
            _cierreIntencionado = true;
            await _conexion.StopAsync();
            _cierreIntencionado = false;

            return new ResultadoConexion(error, HayServidor: true);
        }

        // Identidad de cara a la reconexión y de cara a la UI (barra y panel)
        _usuarioActual = usuario;
        _nick.OnNext(usuario);
        _canalActual.OnNext(_config.EncontrarCanal(canal));
        _estado.OnNext(EstadoConexion.Conectado);
        _enSesion.OnNext(true);

        return new ResultadoConexion(null, HayServidor: true);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<string?> CambiarNickAsync(string nuevoNick)
    {
        try
        {
            var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.CambiarNick, nuevoNick);

            if (error is null)
            {
                // La reconexión y la barra usarán el nick nuevo
                _usuarioActual = nuevoNick;
                _nick.OnNext(nuevoNick);
            }

            return error;
        }
        catch (Exception)
        {
            return ErrorSinConexion;
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

            // Mismo traductor que la terminal: en pantalla, palabras de
            // usuario — jamás el tipo de la excepción
            _log.LogWarning(ex, "No se pudo reconectar con {UrlHub}: {Motivo}", _urlHub, ex.Message);
            return $"No se pudo reconectar: {MensajesError.DescripcionDe(ex)}";
        }
    }

    /// <inheritdoc />
    public async Task CerrarAsync()
    {
        try
        {
            _cierreIntencionado = true; // cierre pedido: Closed no avisa
            await _conexion.StopAsync();
            _cierreIntencionado = false;
        }
        catch
        {
            // La conexión ya estaba caída: nada que cerrar, y una salida
            // jamás debe lanzar excepción
        }

        _estado.OnNext(EstadoConexion.Desconectado);
        _enSesion.OnNext(false);

        // Se vacía la vista: si otra persona entra después de Salir en este
        // mismo navegador, no debe ver los mensajes del anterior NI un
        // destello de ellos antes de que llegue el historial del servidor
        _mensajes.OnNext([]);
        _usuarios.OnNext([]);
    }

    /// <inheritdoc />
    public void LimpiarVista()
    {
        // Solo la vista local: el servidor guarda el historial igual que
        // antes (vuelve al cambiar de canal o reconectar)
        _mensajes.OnNext([]);
    }

    /// <inheritdoc />
    public void Aviso(string texto)
    {
        // Mismo formato que los mensajes de sistema del servidor: quien lo
        // pinte no distingue si vino del hub o de aquí
        var aviso = new MensajeDto(
            Guid.NewGuid(), MensajesSistema.Autor, texto, DateTime.UtcNow, TipoMensaje.Sistema);

        _mensajes.OnNext([.. _mensajes.Value, aviso]);
    }

    /// <summary>
    /// Tras una reconexión automática de SignalR: el transporte vuelve SOLO,
    /// pero el ALTA no. Al caer, el servidor ya ejecutó OnDisconnectedAsync
    /// (nos dio de baja y nos quitó del grupo del canal), y la conexión nueva
    /// tiene OTRO ConnectionId que no está en ningún grupo. Sin este paso, el
    /// cliente quedaría de escayola: la barra dice "conectado" pero nadie nos
    /// ve y a nosotros no nos llega nada.
    /// </summary>
    private async Task ReenviarAltaAsync()
    {
        _estado.OnNext(EstadoConexion.Conectado);

        // Aún no hay identidad (la conexión se cayó antes del primer alta)
        if (_usuarioActual is null || _canalActual.Value is null)
            return;

        try
        {
            var error = await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.Conectar, _usuarioActual, _canalActual.Value);

            // El servidor re-registra y RE-ENVÍA historial, lista y
            // estadísticas; este aviso solo confirma el alta local
            Aviso(error is null
                ? $"Reconectado: vuelto a entrar en {_canalActual.Value} como {_usuarioActual}."
                : $"No pude volver a registrar la sesión: {error}");
        }
        catch (Exception ex)
        {
            // Se cayó otra vez mientras re-registrábamos: el bucle de la
            // conexión ya se encarga de reconectar (Reconnecting/Reconnected)
            _log.LogWarning(ex, "Conexión caída al reenviar el alta con {UrlHub}", _urlHub);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _mensajes.Dispose();
        _usuarios.Dispose();
        _estadisticas.Dispose();
        _canalActual.Dispose();
        _nick.Dispose();
        _estado.Dispose();
        _enSesion.Dispose();

        // DisposeAsync en segundo plano: estamos saliendo y no queremos
        // bloquear el hilo principal en el Dispose
        _ = _conexion.DisposeAsync();
    }
}
