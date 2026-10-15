using System.Reactive.Subjects;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
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
    private readonly BehaviorSubject<IReadOnlyList<UsuarioConectadoDto>> _usuarios = new([]);
    private readonly BehaviorSubject<EstadisticasDto> _estadisticas = new(new EstadisticasDto(0, []));
    private readonly BehaviorSubject<string?> _canalActual = new(null);
    private readonly BehaviorSubject<EstadoConexion> _estado = new(EstadoConexion.Desconectado);

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

        _conexion.Reconnected += _ =>
        {
            _estado.OnNext(EstadoConexion.Conectado);
            return Task.CompletedTask;
        };

        _conexion.Closed += _ =>
        {
            _estado.OnNext(EstadoConexion.Desconectado);
            return Task.CompletedTask;
        };
    }

    // ── Observables (para suscribirse) ──

    /// <summary>Vista actual del canal: historial + mensajes en vivo.</summary>
    public IObservable<IReadOnlyList<MensajeDto>> Mensajes => _mensajes;

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
    /// <returns>null si todo fue correcto; el error del servidor o de red si no.</returns>
    public async Task<string?> ConectarAsync(string usuario, string canal)
    {
        try
        {
            _estado.OnNext(EstadoConexion.Conectando);
            await _conexion.StartAsync();
        }
        catch (Exception ex)
        {
            // No hay servidor, hay un firewall... el mensaje es para el alumno
            _estado.OnNext(EstadoConexion.Desconectado);

            return $"No se pudo conectar con el servidor ({ex.GetType().Name}). ¿Está arrancado?";
        }

        var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.Conectar, usuario, canal);

        if (error is not null)
        {
            // Registro rechazado (p. ej. nombre en uso): cerramos para que
            // el llamador pueda preguntar de nuevo sin estados colgados
            _estado.OnNext(EstadoConexion.Desconectado);
            await _conexion.StopAsync();

            return error;
        }

        _canalActual.OnNext(_config.EncontrarCanal(canal));
        _estado.OnNext(EstadoConexion.Conectado);

        return null;
    }

    /// <summary>
    /// Difunde un mensaje al canal actual.
    /// </summary>
    /// <param name="texto">Texto ya recortado.</param>
    /// <returns>null si todo fue correcto; el error del servidor si no.</returns>
    public async Task<string?> EnviarAsync(string texto) =>
        await _conexion.InvokeAsync<string?>(ChatHubMetodos.Enviar, texto);

    /// <summary>
    /// Cambia al canal dado; el historial del nuevo canal llegará después
    /// por el evento HistorialRecibido y reemplazará la vista.
    /// </summary>
    /// <param name="canal">Canal de destino (se valida localmente antes).</param>
    /// <returns>null si el cambio fue correcto; el error del servidor si no.</returns>
    public async Task<string?> CambiarCanalAsync(string canal)
    {
        var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.CambiarCanal, canal);

        if (error is null)
            _canalActual.OnNext(_config.EncontrarCanal(canal));

        return error;
    }

    /// <summary>
    /// Cambia el nombre en caliente dentro del canal actual.
    /// </summary>
    /// <param name="nuevoNick">Nuevo nombre (ya validado localmente).</param>
    /// <returns>null si el cambio fue correcto; el error del servidor si no.</returns>
    public async Task<string?> CambiarNickAsync(string nuevoNick) =>
        await _conexion.InvokeAsync<string?>(ChatHubMetodos.CambiarNick, nuevoNick);

    /// <summary>
    /// Consulta los conectados de CUALQUIER canal: el servidor responde SOLO
    /// a este cliente (no difunde nada), igual que un GET de una API.
    /// </summary>
    /// <param name="canal">Canal a consultar (ya validado por el llamador).</param>
    /// <returns>La lista de esa sala, o null si el servidor no conoce el canal.</returns>
    public async Task<IReadOnlyList<UsuarioConectadoDto>?> ObtenerUsuariosAsync(string canal) =>
        await _conexion.InvokeAsync<IReadOnlyList<UsuarioConectadoDto>?>(
            ChatHubMetodos.ObtenerUsuarios, canal);

    /// <summary>
    /// Cierra la conexión de forma ordenada (el servidor despide al usuario
    /// en su canal con OnDisconnectedAsync).
    /// </summary>
    public async Task CerrarAsync()
    {
        try
        {
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
        _usuarios.Dispose();
        _estadisticas.Dispose();
        _canalActual.Dispose();
        _estado.Dispose();

        // DisposeAsync en segundo plano: estamos saliendo y no queremos
        // bloquear el hilo principal en el Dispose
        _ = _conexion.DisposeAsync();
    }
}
