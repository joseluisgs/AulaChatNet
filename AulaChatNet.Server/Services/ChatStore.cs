using System.Collections.Concurrent;
using AulaChatNet.Server.Models;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Services;

/// <summary>
/// Implementación en memoria del estado de los canales (Fase 1). Sin base de
/// datos: todo vive en colecciones concurrentes que mueren al parar el servidor.
///
/// <remarks>
/// Estructuras internas y por qué así:
/// <list type="bullet">
/// <item><c>_conexiones</c> (connectionId → canal + usuario): la ÚNICA fuente
/// de verdad de "quién está conectado y dónde"; responde en O(1).</item>
/// <item><c>_canales</c> (canal → <see cref="EstadoCanal"/>): unicidad de
/// nombres e historial SEPARADOS por canal; su <c>TryAdd</c> de nombres da
/// la unicidad de forma atómica, sin bloqueos.</item>
/// </list>
/// Un mismo <see cref="UsuarioConectado"/> se reutiliza al cambiar de canal,
/// conservando así la hora de entrada de la sesión.
/// </remarks>
/// </summary>
public class ChatStore(IOptions<ChatConfig> opciones) : IChatStore
{
    private const string ErrorConexionYaRegistrada = "Esta conexión ya está registrada en el canal";
    private const string ErrorSinRegistro = "No estás registrado en el canal, vuelve a conectarte";

    private readonly ConcurrentDictionary<string, EstadoCanal> _canales = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Conexion> _conexiones = new();

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
        while (estado.Historial.Count > Config.HistorialMaximo && estado.Historial.TryDequeue(out _))
        {
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MensajeDto> GetHistorial(string canal) =>
        _canales.TryGetValue(canal, out var estado) ? estado.Historial.ToArray() : [];

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
