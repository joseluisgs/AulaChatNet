using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Server.Services;

/// <summary>
/// Estado del canal en memoria (Fase 1): usuarios conectados e historial.
/// Es <c>singleton</c> porque el estado es de TODA la aplicación: todos los
/// hubs que crea SignalR comparten la misma instancia.
///
/// <remarks>
/// Convención de devolución, igual que <c>ChatConfig.ValidarUsuario</c>:
/// los métodos que registran devuelven <c>string?</c> donde null significa
/// "todo correcto" y el texto es el mensaje de error en español para el
/// cliente. Un solo formato de error en todo el sistema.
/// </remarks>
/// </summary>
public interface IChatStore
{
    /// <summary>
    /// Registra un usuario en el canal si su nombre no está en uso.
    /// </summary>
    /// <param name="usuario">Nombre de usuario (ya validado y recortado por el hub).</param>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <returns>
    /// null si el registro fue correcto; mensaje de error si el nombre ya
    /// está en uso o la conexión ya estaba registrada.
    /// </returns>
    string? AddUsuario(string usuario, string connectionId);

    /// <summary>
    /// Añade un mensaje al historial; si se supera el límite configurado,
    /// se descarta el más antiguo (cola circular).
    /// </summary>
    /// <param name="mensaje">Mensaje ya construido (normal o de sistema).</param>
    void AddMensaje(MensajeDto mensaje);

    /// <summary>
    /// Devuelve el historial completo: los últimos N mensajes (límite de
    /// <c>ChatConfig.HistorialMaximo</c>), del más antiguo al más reciente.
    /// </summary>
    /// <returns>Copia instantánea del historial.</returns>
    IReadOnlyList<MensajeDto> GetHistorial();

    /// <summary>
    /// Devuelve la lista de usuarios conectados, ordenada por nombre.
    /// </summary>
    /// <returns>Lista para pintar el panel de "en línea".</returns>
    IReadOnlyList<UsuarioConectadoDto> GetUsuarios();

    /// <summary>
    /// Devuelve el nombre del usuario registrado en una conexión.
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <returns>El nombre, o null si esa conexión aún no se ha registrado.</returns>
    string? GetUsuarioDeConexion(string connectionId);

    /// <summary>
    /// Elimina a un usuario al desconectar.
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <returns>El nombre que acaba de salir, o null si esa conexión no estaba registrada.</returns>
    string? RemoveUsuario(string connectionId);
}
