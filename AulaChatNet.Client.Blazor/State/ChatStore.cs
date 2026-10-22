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
    private readonly BehaviorSubject<IReadOnlyList<MensajePrivadoDto>> _privados = new([]);
    private readonly BehaviorSubject<IReadOnlyList<UsuarioConectadoDto>> _usuarios = new([]);
    private readonly BehaviorSubject<EstadisticasDto> _estadisticas = new(new EstadisticasDto(0, []));
    private readonly BehaviorSubject<string?> _canalActual = new(null);
    private readonly BehaviorSubject<string?> _nick = new(null);
    private readonly BehaviorSubject<EstadoConexion> _estado = new(EstadoConexion.Desconectado);
    private readonly BehaviorSubject<bool> _enSesion = new(false);
    private readonly BehaviorSubject<string?> _destinatarioPrivado = new(null);

    // Menciones dirigidas (@ana): la ÚLTIMA recibida (para el parpadeo de
    // la pestaña) y el contador de las que aún no se han mirado (para el
    // badge). El "no visto" es estado DEL CLIENTE: el servidor solo avisa
    private readonly BehaviorSubject<MencionDto?> _ultimaMencion = new(null);
    private readonly BehaviorSubject<int> _mencionesNoVistas = new(0);

    // Paginación del historial (Fase 4.3): si puede quedar algo anterior a
    // lo que ya se ve, y si ahora mismo hay una petición en vuelo (para que
    // el scroll no dispare una segunda mientras la primera no vuelve)
    private readonly BehaviorSubject<bool> _hayAnteriores = new(true);
    private bool _cargandoAnteriores;

    // Reacciones por mensaje: el servidor manda el estado COMPLETO de cada
    // mensaje, así que aquí solo se REEMPLAZA la entrada que cambia. La
    // clave es el Id del mensaje (GUID), el mismo que identifica la fila
    private readonly BehaviorSubject<IReadOnlyDictionary<Guid, ReaccionesMensajeDto>> _reacciones =
        new(new Dictionary<Guid, ReaccionesMensajeDto>());

    // "Está escribiendo...": por un lado QUIÉN, con su caducidad dentro del
    // seguimiento, y por otro el muestreo de las TECLAS propias, que convierte
    // cada pulsación en como mucho una señal por segundo
    private readonly SeguimientoEscritura _seguimiento = new();
    private readonly SenalEscritura _senalEscritura;

    // Último nick y canal con los que se logró registrar: la reconexión los
    // reutiliza para volver a darse de alta sin volver a preguntar al usuario
    private string? _usuarioActual;

    // true mientras la conexión se cierra A PROPÓSITO (botón Salir o
    // registro rechazado): el evento Closed salta también en esos casos y
    // no queremos un aviso de "conexión perdida" cuando la perdimos nosotros.
    // Ojo: SignalR invoca Closed de forma ASÍNCRONA, a veces DESPUÉS de que
    // StopAsync devuelva, así que el flag NO se resetea al cerrar — solo lo
    // hace ConectarAsync/ReconectarAsync al empezar la siguiente conexión.
    // Si no, el cierre tardío se encontraría el flag ya en false (un warning
    // de log justo después de un rechazo de nick).
    private bool _cierreIntencionado;

    /// <summary>Error devuelto al invocar el hub con la conexión caída.</summary>
    private const string ErrorSinConexion = "Sin conexión con el servidor";

    /// <summary>
    /// Tamaño de página que pide el cliente al cargar mensajes antiguos. El
    /// servidor lo limita además a su tope configurado, así que este número
    /// es solo una petición, no un contrato.
    /// </summary>
    private const int TamanoPagina = 25;

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

            // Quien acaba de mandar un mensaje ya no "está escribiendo": se
            // le apaga al instante en vez de esperar a la caducidad (en un
            // mensaje de sistema el nombre es el de la casa y no pinta nada)
            _seguimiento.Quitar(mensaje.Usuario);
        });

        // Privados: llegan SOLO a los dos implicados (el servidor los emite a
        // dos conexiones concretas, nunca al grupo). Van a SU lista y no a la
        // del canal: por eso el historial de la sala jamás los contiene.
        _conexion.On<MensajePrivadoDto>(ChatHubEventos.PrivadoRecibido, privado =>
        {
            _privados.OnNext([.. _privados.Value, privado]);
            _seguimiento.Quitar(privado.Emisor);
        });

        // "Está escribiendo...": viene SOLO el nombre de quien escribe (este
        // evento jamás lleva texto) y solo si no eres tú (OthersInGroup)
        _conexion.On<string>(ChatHubEventos.Escribiendo, usuario => _seguimiento.Senalar(usuario));

        // Menciones dirigidas (@ana): un aviso SEPARADO del mensaje — el
        // mensaje ya llega por MensajeNuevo y aquí se pinta resaltado; esto
        // es solo el empujoncito para que el cliente parpadee y ponga su
        // badge. Llega después del mensaje, nunca antes
        _conexion.On<MencionDto>(ChatHubEventos.MencionRecibida, mencion =>
        {
            _ultimaMencion.OnNext(mencion);
            _mencionesNoVistas.OnNext(_mencionesNoVistas.Value + 1);
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

            // La paginación vuelve a empezar: la vista nueva acaba de
            // llegar y puede tener detrás (el servidor dirá si no queda nada)
            _hayAnteriores.OnNext(true);

            // Con él se vacía también el seguimiento: nadie de la sala
            // anterior debe asomar en la nueva ni un instante
            _seguimiento.Limpiar();

            // Y las reacciones también: el snapshot del nuevo canal llegará
            // acto seguido por su propio evento y rellenará el mapa de cero
            _reacciones.OnNext(new Dictionary<Guid, ReaccionesMensajeDto>());
        });

        // Reacciones de UN mensaje: estado COMPLETO que manda el servidor —
        // aquí solo se reemplaza la entrada de ese mensaje, o se borra si la
        // lista llega vacía (alguien quitó la última reacción)
        _conexion.On<ReaccionesMensajeDto>(ChatHubEventos.ReaccionActualizada, estado =>
        {
            var nuevas = new Dictionary<Guid, ReaccionesMensajeDto>(_reacciones.Value);

            if (estado.Reacciones.Count == 0)
                nuevas.Remove(estado.IdMensaje);
            else
                nuevas[estado.IdMensaje] = estado;

            _reacciones.OnNext(nuevas);
        });

        // Snapshot al entrar o cambiar de canal: REEMPLAZA el mapa entero,
        // para que no sobreviva ningún chip del canal anterior
        _conexion.On<IReadOnlyList<ReaccionesMensajeDto>>(
            ChatHubEventos.ReaccionesRecibidas, lista =>
            {
                var nuevas = new Dictionary<Guid, ReaccionesMensajeDto>();

                foreach (var estado in lista)
                    nuevas[estado.IdMensaje] = estado;

                _reacciones.OnNext(nuevas);
            });

        // Un mensaje del canal ha cambiado de texto (Fase 4.4): llega el
        // mensaje COMPLETO con su Id, así que la vista solo SUSTITUYE la
        // fila que coincide — misma posición, mismo orden, texto nuevo y
        // marca de editado. Si el Id no está en la vista (una página que
        // aún no se pidió), no hay nada que tocar: la próxima carga traerá
        // el texto ya editado, porque el servidor solo guarda uno.
        _conexion.On<MensajeDto>(ChatHubEventos.MensajeEditado, editado =>
        {
            var vista = _mensajes.Value;

            if (!vista.Any(m => m.Id == editado.Id))
                return;

            _mensajes.OnNext([.. vista.Select(m => m.Id == editado.Id ? editado : m)]);
        });

        // Un mensaje del canal se ha borrado: solo hace falta el Id.
        // Quitarlo de la vista, y también SUS chips del mapa de
        // reacciones: sin mensaje que los muestre, el mapa los retendría
        // en contra de su dueño (mismo criterio con el que el servidor
        // los libera al borrar la fila)
        _conexion.On<Guid>(ChatHubEventos.MensajeBorrado, idBorrado =>
        {
            _mensajes.OnNext([.. _mensajes.Value.Where(m => m.Id != idBorrado)]);

            var chips = new Dictionary<Guid, ReaccionesMensajeDto>(_reacciones.Value);

            if (chips.Remove(idBorrado))
                _reacciones.OnNext(chips);
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

        // Muestreo de las teclas propias: Sample emite como mucho una vez
        // por segundo mientras se escriba (ver SenalEscritura). Se crea aquí
        // porque necesita el método que habla con el hub
        _senalEscritura = new SenalEscritura(EnviarSenalEscribiendoAsync);
    }

    /// <inheritdoc />
    public IObservable<IReadOnlyList<MensajeDto>> Mensajes => _mensajes;

    /// <inheritdoc />
    public IObservable<IReadOnlyDictionary<Guid, ReaccionesMensajeDto>> Reacciones => _reacciones;

    /// <inheritdoc />
    public IObservable<IReadOnlyList<MensajePrivadoDto>> Privados => _privados;

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
    public IObservable<string?> DestinatarioPrivado => _destinatarioPrivado;

    /// <summary>Última mención dirigida recibida (null = ninguna esta sesión).</summary>
    public IObservable<MencionDto?> UltimaMencion => _ultimaMencion;

    /// <summary>Menciones recibidas que aún no se han mirado (el badge de la barra).</summary>
    public IObservable<int> MencionesNoVistas => _mencionesNoVistas;

    /// <inheritdoc />
    public IObservable<bool> HayAnteriores => _hayAnteriores;

    /// <summary>
    /// Marca las menciones como vistas: el badge vuelve a cero. El clic
    /// viene del badge, que no puede tocar la store por su cuenta.
    /// </summary>
    public void MarcarMencionesVistas() => _mencionesNoVistas.OnNext(0);

    /// <inheritdoc />
    public IReadOnlyList<string> Canales => _config.Canales;

    /// <inheritdoc />
    public string? Canal => _canalActual.Value;

    /// <inheritdoc />
    public string? VistaNick => _nick.Value;

    /// <inheritdoc />
    public IReadOnlyList<MensajeDto> VistaMensajes => _mensajes.Value;

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, ReaccionesMensajeDto> VistaReacciones => _reacciones.Value;

    /// <inheritdoc />
    public IReadOnlyList<UsuarioConectadoDto> VistaUsuarios => _usuarios.Value;

    /// <inheritdoc />
    public IReadOnlyList<MensajePrivadoDto> VistaPrivados => _privados.Value;

    /// <inheritdoc />
    public EstadisticasDto EstadisticasActuales => _estadisticas.Value;

    /// <inheritdoc />
    public EstadoConexion EstadoValor => _estado.Value;

    /// <inheritdoc />
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
            // así que Closed no lanza ningún aviso — y como ese evento puede
            // llegar DESPUÉS de que StopAsync devuelva, el flag NO se
            // resetea aquí (lo hará la próxima ConectarAsync): así no hay
            // aviso ni warning fantasma tras un rechazo.
            _log.LogWarning("El hub rechazó a {Usuario} en {Canal}: {Error}", usuario, canal, error);
            _estado.OnNext(EstadoConexion.Desconectado);
            _cierreIntencionado = true;
            await _conexion.StopAsync();

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
            var error = await _conexion.InvokeAsync<string?>(ChatHubMetodos.Enviar, texto);

            // El mensaje salió: se corta la señal de escritura de la ventana
            // en curso (si no, llegaría un instante después y repondría al
            // autor en la lista de "escribiendo" con el mensaje ya en pantalla)
            if (error is null)
                _senalEscritura.Envio();

            return error;
        }
        catch (Exception)
        {
            // Conexión caída: InvokeAsync LANZA en vez de devolver el error
            // del servidor, así que lo convertimos al mismo patrón string?
            return ErrorSinConexion;
        }
    }

    /// <inheritdoc />
    public async Task<string?> EnviarPrivadoAsync(string destinatario, string texto)
    {
        try
        {
            var error = await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.EnviarPrivado, destinatario, texto);

            // Mismo corte de señal que un mensaje normal: el privado salió
            if (error is null)
                _senalEscritura.Envio();

            return error;
        }
        catch (Exception)
        {
            // Mismo traductor que EnviarAsync: sin conexión, error en español
            return ErrorSinConexion;
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReaccionarAsync(Guid idMensaje, string emoji)
    {
        try
        {
            // El eco vuelve por REACCION ACTUALIZADA: la vista se pinta SOLO
            // desde ahí, igual que con los mensajes — el cliente no calcula nada
            return await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.Reaccionar, idMensaje, emoji);
        }
        catch (Exception)
        {
            return ErrorSinConexion;
        }
    }

    /// <inheritdoc />
    public async Task<string?> EditarMensajeAsync(Guid idMensaje, string texto)
    {
        try
        {
            // El eco vuelve por MENSAJE EDITADO con el mensaje completo: la
            // store solo sustituye la fila que cambia — mismo camino de
            // reemplazo que los mensajes y las reacciones
            return await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.Editar, idMensaje, texto);
        }
        catch (Exception)
        {
            return ErrorSinConexion;
        }
    }

    /// <inheritdoc />
    public async Task<string?> BorrarMensajeAsync(Guid idMensaje)
    {
        try
        {
            // El eco vuelve por MENSAJE BORRADO con solo el Id: la store
            // quita la fila y sus chips de la vista
            return await _conexion.InvokeAsync<string?>(
                ChatHubMetodos.Borrar, idMensaje);
        }
        catch (Exception)
        {
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
    public async Task CargarAnterioresAsync()
    {
        // Sin vista no hay cursor, y con una petición en vuelo no se pide otra
        if (_cargandoAnteriores || _mensajes.Value.Count == 0)
            return;

        _cargandoAnteriores = true;

        try
        {
            // Cursor = el mensaje más antiguo de la vista. El servidor
            // devuelve lo anterior (inclusive) a ese instante, nada más
            var cursor = _mensajes.Value[0].Timestamp;

            var pagina = await _conexion.InvokeAsync<IReadOnlyList<MensajeDto>?>(
                ChatHubMetodos.Historial, cursor, TamanoPagina);

            if (pagina is null || pagina.Count == 0)
            {
                // Página vacía = no quedaba más historial. null no debería
                // pasar (este botón solo existe en sesión), pero corta el
                // bucle igual: sin datos no hay nada que pintar
                _hayAnteriores.OnNext(false);
                return;
            }

            var actuales = _mensajes.Value;
            var yaVistas = actuales.Select(m => m.Id).ToHashSet();

            // El servidor devuelve también el del cursor (el rango es
            // inclusivo) y una página podría solaparse; el descarte por Id
            // lo deja inocuo
            var nuevas = pagina.Where(m => !yaVistas.Contains(m.Id)).ToArray();

            if (nuevas.Length == 0)
            {
                _hayAnteriores.OnNext(false);
                return;
            }

            // Por delante: la página vieja va ARRIBA de todo lo que ya se ve
            _mensajes.OnNext([.. nuevas, .. actuales]);
        }
        catch (Exception)
        {
            // Sin conexión: la vista no crece; el aviso de cierre llega por
            // su propio camino (jamás lanzar desde un clic o un scroll)
        }
        finally
        {
            _cargandoAnteriores = false;
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
            // Cierre pedido: Closed no avisa. El flag NO se resetea al
            // terminar (SignalR puede entregar Closed tardío); lo hará la
            // próxima ConectarAsync, al empezar la siguiente conexión.
            _cierreIntencionado = true;
            await _conexion.StopAsync();
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
        _privados.OnNext([]); // y los privados menos: no hay servidor que los vuelva a traer
        _reacciones.OnNext(new Dictionary<Guid, ReaccionesMensajeDto>()); // ni chips de nadie

        // Ni las menciones sin mirar de la sesión anterior: el badge de la
        // siguiente persona que entre en este navegador empieza en cero
        _mencionesNoVistas.OnNext(0);
        _ultimaMencion.OnNext(null);

        // Y nadie de la sesión anterior debe seguir "escribiendo"
        _seguimiento.Limpiar();

        // Y se cierra el privado abierto: si otra persona entra después en
        // este navegador, no debe empezar escribiéndole al anterior
        _destinatarioPrivado.OnNext(null);
    }

    /// <inheritdoc />
    public void LimpiarVista()
    {
        // Solo la vista local: el servidor guarda el historial igual que
        // antes (vuelve al cambiar de canal o reconectar)
        _mensajes.OnNext([]);
    }

    /// <inheritdoc />
    public void PrepararPrivado(string destinatario)
    {
        _destinatarioPrivado.OnNext(destinatario);
    }

    /// <inheritdoc />
    public IObservable<IReadOnlyList<string>> Escribiendo => _seguimiento.Activos;

    /// <inheritdoc />
    public void SenalarEscritura() => _senalEscritura.Tecla();

    /// <summary>
    /// Manda la señal de escritura al hub, en segundo plano y en silencio:
    /// es un aviso ornamental, no una operación con resultado que notificar.
    /// Si no hay conexión, no pasa nada — la próxima tecla lo reintentará.
    /// </summary>
    private async Task EnviarSenalEscribiendoAsync()
    {
        // Solo con la conexión en verde: mientras se reconecta, la señal no
        // tiene dónde llegar
        if (EstadoValor != EstadoConexion.Conectado)
            return;

        try
        {
            await _conexion.InvokeAsync<string?>(ChatHubMetodos.Escribiendo);
        }
        catch (Exception)
        {
            // La conexión se cayó justo entre la comprobación y la llamada
        }
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
        _privados.Dispose();
        _usuarios.Dispose();
        _estadisticas.Dispose();
        _canalActual.Dispose();
        _nick.Dispose();
        _estado.Dispose();
        _enSesion.Dispose();
        _destinatarioPrivado.Dispose();
        _hayAnteriores.Dispose();
        _reacciones.Dispose();

        // El seguimiento de escritura también: su latido de caducidad vive en
        // un temporizador que debe pararse con la store
        _senalEscritura.Dispose();
        _seguimiento.Dispose();

        // DisposeAsync en segundo plano: estamos saliendo y no queremos
        // bloquear el hilo principal en el Dispose
        _ = _conexion.DisposeAsync();
    }
}
