using System.Collections.Concurrent;
using AulaChatNet.Server.Models;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Services;

/// <summary>
/// Implementación en memoria del estado del chat (Fase 1). Sin base de datos:
/// todo vive en colecciones concurrentes que mueren al parar el servidor.
///
/// <remarks>
/// Estructuras internas y por qué dos diccionarios:
/// <list type="bullet">
/// <item><c>_usuariosPorConnection</c> (connectionId → usuario): responde
/// "¿quién soy yo?" al enviar un mensaje o desconectar, en O(1).</item>
/// <item><c>_usuariosPorNombre</c> (clave normalizada → usuario): su
/// <c>TryAdd</c> da la unicidad de nombres de forma ATÓMICA: si dos personas
/// escriben "Ana" a la vez, solo una gana sin necesidad de bloqueos.</item>
/// <item><c>_historial</c>: cola circular con el límite de
/// <c>ChatConfig.HistorialMaximo</c>.</item>
/// </list>
/// </remarks>
/// </summary>
public class ChatStore(IOptions<ChatConfig> opciones) : IChatStore
{
    private const string ErrorConexionYaRegistrada = "Esta conexión ya está registrada en el canal";

    private readonly ConcurrentDictionary<string, UsuarioConectado> _usuariosPorConnection = new();
    private readonly ConcurrentDictionary<string, UsuarioConectado> _usuariosPorNombre = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<MensajeDto> _historial = new();

    // Las opciones son singleton: leerlas en cada operación refleja cambios
    // de configuración sin recrear el store.
    private ChatConfig Config => opciones.Value;

    /// <inheritdoc />
    public string? AddUsuario(string usuario, string connectionId)
    {
        // Chequeo rápido previo: una conexión no puede darse de alta dos veces
        if (_usuariosPorConnection.ContainsKey(connectionId))
            return ErrorConexionYaRegistrada;

        // Clave de unicidad normalizada: "ANA", "Ana" y "ana" son el mismo nombre
        var clave = Normalizar(usuario);
        var nuevo = new UsuarioConectado(usuario, DateTime.UtcNow);

        // TryAdd atómico: si otra persona llegó primero con ese nombre, aquí
        // se rechaza sin necesidad de bloquear nada
        if (!_usuariosPorNombre.TryAdd(clave, nuevo))
            return Config.ErrorUsuarioDuplicado;

        // Carrera posible: dos conexiones registrando el mismo nombre a la vez.
        // Solo una gana en el índice de nombres; la otra falla aquí y debe
        // deshacer su alta para no dejar un nombre "fantasma" bloqueado.
        if (!_usuariosPorConnection.TryAdd(connectionId, nuevo))
        {
            _usuariosPorNombre.TryRemove(
                new KeyValuePair<string, UsuarioConectado>(clave, nuevo));

            return ErrorConexionYaRegistrada;
        }

        return null;
    }

    /// <inheritdoc />
    public void AddMensaje(MensajeDto mensaje)
    {
        _historial.Enqueue(mensaje);

        // Cola circular: al superar el límite configurado, el más antiguo
        // sale del historial (el bucle contempla que el límite sea pequeño).
        while (_historial.Count > Config.HistorialMaximo && _historial.TryDequeue(out _))
        {
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MensajeDto> GetHistorial() => _historial.ToArray();

    /// <inheritdoc />
    public IReadOnlyList<UsuarioConectadoDto> GetUsuarios() =>
        _usuariosPorConnection.Values
            .OrderBy(u => u.Usuario, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new UsuarioConectadoDto(u.Usuario, u.ConectadoDesde))
            .ToArray();

    /// <inheritdoc />
    public string? GetUsuarioDeConexion(string connectionId) =>
        _usuariosPorConnection.TryGetValue(connectionId, out var usuario) ? usuario.Usuario : null;

    /// <inheritdoc />
    public string? RemoveUsuario(string connectionId)
    {
        if (!_usuariosPorConnection.TryRemove(connectionId, out var usuario))
            return null;

        // TryRemove por par clave/valor: solo borra si el registro sigue siendo
        // el mismo; así nunca se borra por accidente el alta de otra conexión
        // que haya reaprovechado el nombre tras una salida concurrente.
        _usuariosPorNombre.TryRemove(
            new KeyValuePair<string, UsuarioConectado>(Normalizar(usuario.Usuario), usuario));

        return usuario.Usuario;
    }

    /// <summary>
    /// Normaliza un nombre para usarlo como clave de unicidad.
    /// </summary>
    /// <param name="usuario">Nombre de usuario.</param>
    /// <returns>Nombre recortado y en mayúsculas (clave estable).</returns>
    private static string Normalizar(string usuario) => usuario.Trim().ToUpperInvariant();
}
