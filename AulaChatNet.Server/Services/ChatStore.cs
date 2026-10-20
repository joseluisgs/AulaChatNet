using System.Collections.Concurrent;
using System.Threading.Channels;
using AulaChatNet.Server.Models;
using AulaChatNet.Server.Persistencia;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Services;

/// <summary>
/// Estado de los canales (Fase 1 y 2): usuarios conectados e historial, separados
/// POR CANAL.
///
/// <remarks>
/// <para>
/// La PRESENCIA y la UNICIDAD de nombres viven SOLO en memoria (colecciones
/// concurrentes que mueren al parar el servidor): son datos efímeros, anclados a
/// una conexión viva. El HISTORIAL, en cambio, además de cachearse en memoria se
/// PERSISTE en la base de datos (Fase 2), de modo que sobrevive a un reinicio.
/// Esa separación es deliberada: se separa por LONGEVIDAD, no por entidad.
/// </para>
/// <para>
/// Estructuras internas y por qué así:
/// </para>
/// <list type="bullet">
/// <item><c>_conexiones</c> (connectionId → canal + usuario): la ÚNICA fuente
/// de verdad de "quién está conectado y dónde"; responde en O(1).</item>
/// <item><c>_canales</c> (canal → <see cref="EstadoCanal"/>): unicidad de
/// nombres e historial SEPARADOS por canal; su <c>TryAdd</c> de nombres da
/// la unicidad de forma atómica, sin bloqueos.</item>
/// <item><c>_colaEscritura</c>: cola por la que se encolan los mensajes para
/// guardarlos en segundo plano, SIN bloquear el chat (Fase 2).</item>
/// </list>
/// <para>
/// Un mismo <see cref="UsuarioConectado"/> se reutiliza al cambiar de canal,
/// conservando así la hora de entrada de la sesión.
/// </para>
/// </remarks>
/// </summary>
public class ChatStore(IOptions<ChatConfig> opciones, IMensajesRepository? repository = null) : IChatStore
{
    private const string ErrorConexionYaRegistrada = "Esta conexión ya está registrada en el canal";
    private const string ErrorSinRegistro = "No estás registrado en el canal, vuelve a conectarte";

    private readonly ConcurrentDictionary<string, EstadoCanal> _canales = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Conexion> _conexiones = new();

    // Repositorio opcional (Fase 2). Si es null (modo Fase 1, p. ej. en los
    // tests), el store se comporta EXACTAMENTE como antes: solo memoria.
    private readonly IMensajesRepository? _repository = repository;

    // Cola de escritura en segundo plano: AddMensaje encola aquí y el servicio
    // de persistencia la vacía contra la base de datos SIN bloquear el chat.
    // Sin límite (unbounded) a propósito: en un aula el volumen es bajo y no se
    // debe perder ni un mensaje por un atasco de disco.
    private readonly Channel<(string Canal, MensajeDto Mensaje)> _colaEscritura =
        Channel.CreateUnbounded<(string Canal, MensajeDto Mensaje)>();

    // Las opciones son singleton: leerlas en cada operación refleja cambios
    // de configuración sin recrear el store.
    private ChatConfig Config => opciones.Value;

    /// <summary>Registro interno de una conexión viva.</summary>
    /// <param name="Canal">Canal canónico donde está la conexión.</param>
    /// <param name="Estado">Estado del canal (para liberar el nombre al salir).</param>
    /// <param name="Datos">Datos visibles del usuario (nombre y entrada).</param>
    private sealed record Conexion(string Canal, EstadoCanal Estado, UsuarioConectado Datos);

    /// <inheritdoc />
    public string? AddUsuario(string canal, string usuario, string connectionId)
    {
        var estado = _canales.GetOrAdd(canal, _ => new EstadoCanal());

        // Chequeo rápido previo: una conexión no puede darse de alta dos veces
        if (_conexiones.ContainsKey(connectionId))
            return ErrorConexionYaRegistrada;

        // Clave de unicidad normalizada: "ANA", "Ana" y "ana" son el mismo nombre
        var clave = Normalizar(usuario);
        var nuevo = new UsuarioConectado(usuario, DateTime.UtcNow);

        // TryAdd atómico POR CANAL: si alguien llegó primero con ese nombre
        // en este canal, se rechaza sin necesidad de bloquear nada
        if (!estado.UsuariosPorNombre.TryAdd(clave, nuevo))
            return Config.ErrorUsuarioDuplicado;

        // Carrera posible: dos conexiones registrando el mismo nombre a la vez.
        // Solo una gana; la otra deshace su alta para no dejar un nombre fantasma.
        if (!_conexiones.TryAdd(connectionId, new Conexion(canal, estado, nuevo)))
        {
            estado.UsuariosPorNombre.TryRemove(
                new KeyValuePair<string, UsuarioConectado>(clave, nuevo));

            return ErrorConexionYaRegistrada;
        }

        return null;
    }

    /// <inheritdoc />
    public string? CambiarCanal(string connectionId, string canalNuevo)
    {
        if (!_conexiones.TryGetValue(connectionId, out var conexion))
            return ErrorSinRegistro;

        // Mismo canal (ignorando mayúsculas): no hay nada que mover
        if (string.Equals(conexion.Canal, canalNuevo, StringComparison.OrdinalIgnoreCase))
            return null;

        var estadoNuevo = _canales.GetOrAdd(canalNuevo, _ => new EstadoCanal());
        var clave = Normalizar(conexion.Datos.Usuario);

        // Unicidad del nombre en el canal de DESTINO
        if (!estadoNuevo.UsuariosPorNombre.TryAdd(clave, conexion.Datos))
            return Config.ErrorUsuarioDuplicado;

        // Mover la conexión: TryUpdate es atómico y solo actualiza si el
        // registro sigue siendo el mismo (si la conexión se cayó entre tanto,
        // no se mueve nada y se deshace el alta provisional en el destino)
        if (!_conexiones.TryUpdate(connectionId, new Conexion(canalNuevo, estadoNuevo, conexion.Datos), conexion))
        {
            estadoNuevo.UsuariosPorNombre.TryRemove(
                new KeyValuePair<string, UsuarioConectado>(clave, conexion.Datos));

            return ErrorSinRegistro;
        }

        // Liberar el nombre en el canal ANTERIOR (con la misma protección
        // clave/valor: solo borra si sigue siendo ese registro)
        conexion.Estado.UsuariosPorNombre.TryRemove(
            new KeyValuePair<string, UsuarioConectado>(clave, conexion.Datos));

        return null;
    }

    /// <inheritdoc />
    public string? CambiarNick(string connectionId, string nuevoNick)
    {
        if (!_conexiones.TryGetValue(connectionId, out var conexion))
            return ErrorSinRegistro;

        var claveNueva = Normalizar(nuevoNick);
        var claveVieja = Normalizar(conexion.Datos.Usuario);

        // Mismo nombre (solo cambian mayúsculas/espacios): nada que hacer
        if (claveNueva == claveVieja)
            return null;

        // Nuevo nombre visible conservando la hora de entrada de la sesión
        var nuevo = new UsuarioConectado(nuevoNick, conexion.Datos.ConectadoDesde);

        // Unicidad del NUEVO nombre en SU canal actual
        if (!conexion.Estado.UsuariosPorNombre.TryAdd(claveNueva, nuevo))
            return Config.ErrorUsuarioDuplicado;

        // Mover la conexión: conservamos la hora de entrada de la sesión,
        // solo cambia el nombre visible
        if (!_conexiones.TryUpdate(
                connectionId,
                new Conexion(conexion.Canal, conexion.Estado, nuevo),
                conexion))
        {
            conexion.Estado.UsuariosPorNombre.TryRemove(
                new KeyValuePair<string, UsuarioConectado>(claveNueva, nuevo));

            return ErrorSinRegistro;
        }

        // Liberar el nombre viejo (solo si sigue siendo ese registro)
        conexion.Estado.UsuariosPorNombre.TryRemove(
            new KeyValuePair<string, UsuarioConectado>(claveVieja, conexion.Datos));

        return null;
    }

    /// <inheritdoc />
    public void AddMensaje(string canal, MensajeDto mensaje)
    {
        var estado = _canales.GetOrAdd(canal, _ => new EstadoCanal());
        estado.Historial.Enqueue(mensaje);

        // Cola circular: al superar el límite configurado, el más antiguo
        // sale del historial (el bucle contempla que el límite sea pequeño).
        // Lo que se va del historial se lleva SUS reacciones: una reacción
        // sobre un mensaje que ya no se puede ver es basura con dueño perdido.
        while (estado.Historial.Count > Config.HistorialMaximo && estado.Historial.TryDequeue(out var fuera))
        {
            estado.Reacciones.TryRemove(fuera.Id, out _);
        }

        // Persistencia (Fase 2): si hay repositorio, se encola para guardarla
        // en segundo plano. NO se escribe aquí: AddMensaje es síncrono y no
        // debe bloquear esperando disco. El filtro "solo mensajes normales" lo
        // aplica el repositorio (única puerta a la BD). Sin repositorio no se
        // encola nada y la caché se comporta como siempre (modo Fase 1).
        if (_repository is not null)
            _colaEscritura.Writer.TryWrite((canal, mensaje));
    }

    /// <inheritdoc />
    public IReadOnlyList<MensajeDto> GetHistorial(string canal) =>
        _canales.TryGetValue(canal, out var estado) ? estado.Historial.ToArray() : [];

    /// <inheritdoc />
    public ReaccionesMensajeDto? AlternarReaccion(
        string canal, Guid idMensaje, string emoji, string usuario)
    {
        if (!_canales.TryGetValue(canal, out var estado))
            return null;

        // El mensaje debe seguir en la caché Y ser de una persona: los avisos
        // de sistema ("Ana se ha unido") no son mensajes a los que se reaccione.
        // Búsqueda lineal a propósito: la caché está limitada a HistorialMaximo
        // mensajes, y un segundo índice significaría mantener dos sitios al día.
        var mensaje = estado.Historial.FirstOrDefault(m => m.Id == idMensaje);

        if (mensaje is null || mensaje.Tipo == TipoMensaje.Sistema)
            return null;

        var porEmoji = estado.Reacciones.GetOrAdd(
            idMensaje, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>(StringComparer.Ordinal));
        var personas = porEmoji.GetOrAdd(emoji, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));

        var clave = Normalizar(usuario);

        // Si la persona ya había puesto ESTE emoji, se lo quita: el gesto es
        // un interruptor, y la decisión la toca el servidor — los clientes
        // solo invocan. El alta/dictamen es atómico (TryAdd) porque el hub
        // atiende a varias personas a la vez.
        if (!personas.TryAdd(clave, usuario))
        {
            // Borrado POR CLAVE: la clave YA es la forma normalizada de la
            // persona, así que identifica a un individuo con seguridad. Si
            // alguien vuelve con otra grafía ("Ana" y "ANA" son la misma),
            // el borrado par clave/valor no encontraría el valor guardado
            // (que es la grafía original) y la reacción no se quitaría.
            personas.TryRemove(clave, out _);

            // Si ese emoji se quedó SIN NADIE, desaparece el chip entero.
            // Hay que mirar que esté vacío: la protección par clave/valor solo
            // garantiza que el diccionario siga siendo el mismo, NO que esté
            // vacío. Sin esta comprobación, cuando Ana se deshiciera de SU
            // chip con Luis todavía dentro, el emoji entero se iría y la
            // reacción de Luis desaparecería de todas las pantallas.
            // La quita sigue protegida: nunca se borra un emoji que otro ya
            // esté rellenando de nuevo con otra persona.
            if (personas.IsEmpty)
            {
                porEmoji.TryRemove(
                    new KeyValuePair<string, ConcurrentDictionary<string, string>>(emoji, personas));
            }
        }

        var reacciones = CrearDto(idMensaje, porEmoji);

        // Sin ningún chip, el mensaje no necesita entrada: se libera la
        // memoria y el cliente, al recibir la lista vacía, borra la fila
        if (reacciones.Reacciones.Count == 0)
        {
            estado.Reacciones.TryRemove(
                new KeyValuePair<Guid, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>(
                    idMensaje, porEmoji));
        }

        return reacciones;
    }

    /// <inheritdoc />
    public IReadOnlyList<ReaccionesMensajeDto> GetReacciones(string canal)
    {
        if (!_canales.TryGetValue(canal, out var estado))
            return [];

        return estado.Reacciones
            .Select(par => CrearDto(par.Key, par.Value))
            .Where(dto => dto.Reacciones.Count > 0)
            .ToArray();
    }

    /// <summary>
    /// Monta el DTO que sale del servidor para UN mensaje: los chips en el
    /// orden FIJO de la lista cerrada (el chip no debe "baile" al añadirse)
    /// y las personas de cada chip ordenadas alfabéticamente.
    /// </summary>
    /// <param name="idMensaje">Mensaje al que pertenecen.</param>
    /// <param name="porEmoji">Índice interno emoji → personas del mensaje.</param>
    /// <returns>Estado completo de las reacciones del mensaje.</returns>
    private static ReaccionesMensajeDto CrearDto(
        Guid idMensaje,
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>> porEmoji)
    {
        var reacciones = porEmoji
            .Select(par => new ReaccionDto(
                par.Key,
                par.Value.Values
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(r => PosicionEnLista(r.Emoji))
            .ToArray();

        return new ReaccionesMensajeDto(idMensaje, reacciones);
    }

    /// <summary>
    /// Sitio de un emoji en la lista cerrada: así los chips se pintan siempre
    /// en el mismo orden. Un emoji fuera de la lista (no debería existir)
    /// va al final en lugar de romper nada.
    /// </summary>
    /// <param name="emoji">Emoji a situar.</param>
    /// <returns>Posición del emoji en la lista cerrada; int.MaxValue si no está.</returns>
    private static int PosicionEnLista(string emoji)
    {
        var admitidos = EmojisReaccion.Admitidos;

        for (var i = 0; i < admitidos.Count; i++)
        {
            if (string.Equals(admitidos[i], emoji, StringComparison.Ordinal))
                return i;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Lector de la cola de escritura en segundo plano (Fase 2). Lo consume el
    /// <c>PersistenciaMensajesService</c> para pasar los mensajes a la base de
    /// datos; también sirve para inspeccionar la cola en los tests.
    /// </summary>
    /// <remarks>
    /// Se expone el lector, no el escritor: el único que encola es el propio
    /// store (en <see cref="AddMensaje"/>), y el que vacía es el servicio de
    /// persistencia. Así queda claro quién hace qué.
    /// </remarks>
    public ChannelReader<(string Canal, MensajeDto Mensaje)> ColaEscritura => _colaEscritura.Reader;

    /// <summary>
    /// Carga desde la base de datos los últimos mensajes de cada canal configurado
    /// y los deja en la caché en memoria, para que el primer cliente que entre ya
    /// vea el historial (Fase 2).
    /// </summary>
    /// <param name="limite">Mensajes por canal a traer del disco
    /// (<c>Persistencia:PreloadMensajes</c>); se limita además al tamaño de caché.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>Total de mensajes precargados en todos los canales.</returns>
    /// <remarks>
    /// No hace nada si no hay repositorio (modo Fase 1). Los mensajes se meten
    /// DIRECTAMENTE en la caché, sin pasar por la cola de escritura: ya están en
    /// la base de datos, no hay que volver a guardarlos.
    /// </remarks>
    public async Task<int> PrecargarAsync(int limite, CancellationToken cancelacion = default)
    {
        if (_repository is null)
            return 0;

        // No tiene sentido traer más mensajes de los que la caché retiene
        var tope = Math.Min(limite, Config.HistorialMaximo);
        if (tope <= 0)
            return 0;

        var total = 0;

        foreach (var canal in Config.Canales)
        {
            var mensajes = await _repository.GetUltimosAsync(canal, tope, cancelacion);
            if (mensajes.Count == 0)
                continue;

            var estado = _canales.GetOrAdd(canal, _ => new EstadoCanal());

            foreach (var mensaje in mensajes)
                estado.Historial.Enqueue(mensaje);

            // Misma poda que AddMensaje, por si acaso (debería ser innecesario
            // porque tope ya limita lo pedido). Las reacciones vacían con él,
            // aunque al precargar nunca puede haber ninguna todavía.
            while (estado.Historial.Count > Config.HistorialMaximo && estado.Historial.TryDequeue(out var fuera))
            {
                estado.Reacciones.TryRemove(fuera.Id, out _);
            }

            total += mensajes.Count;
        }

        return total;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MensajeDto>> GetAnterioresAsync(
        string canal, DateTime antesDe, int cantidad, CancellationToken cancelacion = default)
    {
        // Sin repositorio no hay dónde mirar: la caché solo retiene lo que el
        // cliente YA ve (los últimos HistorialMaximo), nunca nada anterior
        if (_repository is null || antesDe == default)
            return [];

        // Misma regla que la precarga: una página no supera la ventana del
        // sistema. El Math.Max protege el Clamp por si alguien configurara
        // HistorialMaximo a 0 (mínimo mayor que máximo revienta)
        var tope = Math.Clamp(cantidad, 1, Math.Max(1, Config.HistorialMaximo));

        return await _repository.GetAnterioresAsync(canal, antesDe, tope, cancelacion);
    }

    /// <inheritdoc />
    public IReadOnlyList<UsuarioConectadoDto> GetUsuarios(string canal) =>
        _conexiones.Values
            .Where(c => string.Equals(c.Canal, canal, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Datos)
            .OrderBy(u => u.Usuario, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new UsuarioConectadoDto(u.Usuario, u.ConectadoDesde))
            .ToArray();

    /// <inheritdoc />
    public EstadisticasDto GetEstadisticas()
    {
        // Agrupamos las conexiones vivas por su canal: como CADA conexión está
        // en exactamente UN canal, la suma de los grupos es el total real
        var conteos = _conexiones.Values
            .GroupBy(c => c.Canal, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ConteoCanalDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Conectados)
            .ThenBy(c => c.Canal, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new EstadisticasDto(_conexiones.Count, conteos);
    }

    /// <inheritdoc />
    public ConexionChat? GetConexion(string connectionId) =>
        _conexiones.TryGetValue(connectionId, out var conexion)
            ? new ConexionChat(conexion.Canal, conexion.Datos.Usuario)
            : null;

    /// <inheritdoc />
    public string? GetConnectionId(string canal, string usuario)
    {
        var clave = Normalizar(usuario);

        // Búsqueda lineal a propósito: el mapa está indexado por connectionId
        // (es la única clave que existe), en un aula hay decenas de conectados
        // y una tabla de equivalencia extra significaría un SEGUNDO sitio donde
        // mantener la unicidad de nombres, con el riesgo de que las dos se
        // descuadren. Filtrar por canal no es pereza: es lo que hace que el
        // nombre identifique a una sola persona.
        foreach (var conexion in _conexiones)
        {
            if (string.Equals(conexion.Value.Canal, canal, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Normalizar(conexion.Value.Datos.Usuario), clave, StringComparison.Ordinal))
            {
                return conexion.Key;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public ConexionChat? RemoveUsuario(string connectionId)
    {
        if (!_conexiones.TryRemove(connectionId, out var conexion))
            return null;

        // TryRemove por par clave/valor: solo borra si el registro sigue
        // siendo el mismo (nunca borra por accidente el alta de otra
        // conexión que haya reaprovechado el nombre).
        conexion.Estado.UsuariosPorNombre.TryRemove(
            new KeyValuePair<string, UsuarioConectado>(Normalizar(conexion.Datos.Usuario), conexion.Datos));

        return new ConexionChat(conexion.Canal, conexion.Datos.Usuario);
    }

    /// <summary>
    /// Normaliza un nombre para usarlo como clave de unicidad.
    /// </summary>
    /// <param name="usuario">Nombre de usuario.</param>
    /// <returns>Nombre recortado y en mayúsculas (clave estable).</returns>
    private static string Normalizar(string usuario) => usuario.Trim().ToUpperInvariant();
}
