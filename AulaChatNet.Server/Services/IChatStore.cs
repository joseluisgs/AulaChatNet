using AulaChatNet.Server.Models;
using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Server.Services;

/// <summary>
/// Estado de los canales en memoria (Fase 1): usuarios conectados e
/// historial, separados POR CANAL. Es <c>singleton</c> porque el estado es
/// de TODA la aplicación: todos los hubs comparten la misma instancia.
///
/// <remarks>
/// Convención de devolución, igual que <c>ChatConfig.ValidarUsuario</c>:
/// los métodos que registran devuelven <c>string?</c> donde null significa
/// "todo correcto" y el texto es el mensaje de error en español para el
/// cliente. Un solo formato de error en todo el sistema.
///
/// Los nombres de canal deben llegar en su forma canónica (la de la
/// configuración); es el hub quien los resuelve con
/// <c>ChatConfig.EncontrarCanal</c> antes de llamar al store.
/// </remarks>
/// </summary>
public interface IChatStore
{
    /// <summary>
    /// Registra un usuario en un canal si su nombre no está en uso en ESE canal.
    /// </summary>
    /// <param name="canal">Canal canónico (p. ej. "#general").</param>
    /// <param name="usuario">Nombre de usuario (ya validado y recortado por el hub).</param>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <returns>
    /// null si el registro fue correcto; mensaje de error si el nombre ya
    /// está en uso en el canal o la conexión ya estaba registrada.
    /// </returns>
    string? AddUsuario(string canal, string usuario, string connectionId);

    /// <summary>
    /// Mueve a un usuario de canal: le da de alta en el destino (con la
    /// unicidad de nombre de ese canal) y le libera el nombre en el origen.
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <param name="canalNuevo">Canal canónico de destino.</param>
    /// <returns>
    /// null si el cambio fue correcto; mensaje de error si el nombre ya
    /// está en uso en el destino o la conexión no está registrada.
    /// </returns>
    string? CambiarCanal(string connectionId, string canalNuevo);

    /// <summary>
    /// Cambia el nombre de un usuario registrado DENTRO de su canal actual:
    /// ocupa el nuevo nombre (unicidad del canal) y libera el anterior.
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <param name="nuevoNick">Nuevo nombre (ya validado y recortado por el hub).</param>
    /// <returns>
    /// null si el cambio fue correcto (o el nombre era el mismo); mensaje de
    /// error si el nombre ya está en uso en su canal o la conexión no está registrada.
    /// </returns>
    string? CambiarNick(string connectionId, string nuevoNick);

    /// <summary>
    /// Añade un mensaje al historial del canal; si se supera el límite
    /// configurado, se descarta el más antiguo (cola circular).
    /// </summary>
    /// <param name="canal">Canal canónico.</param>
    /// <param name="mensaje">Mensaje ya construido (normal o de sistema).</param>
    void AddMensaje(string canal, MensajeDto mensaje);

    /// <summary>
    /// Devuelve el historial del canal: los últimos N mensajes (límite de
    /// <c>ChatConfig.HistorialMaximo</c>), del más antiguo al más reciente.
    /// </summary>
    /// <param name="canal">Canal canónico.</param>
    /// <returns>Copia instantánea del historial del canal.</returns>
    IReadOnlyList<MensajeDto> GetHistorial(string canal);

    /// <summary>
    /// Devuelve la lista de usuarios conectados EN UN CANAL, ordenada por nombre.
    /// </summary>
    /// <param name="canal">Canal canónico.</param>
    /// <returns>Lista para pintar el panel de "en línea" del canal.</returns>
    IReadOnlyList<UsuarioConectadoDto> GetUsuarios(string canal);

    /// <summary>
    /// Devuelve las estadísticas globales: total de personas conectadas y
    /// cuántas hay en cada sala (solo las salas con alguien dentro).
    /// </summary>
    /// <returns>Copia instantánea de los contadores, para emitirla a los clientes.</returns>
    EstadisticasDto GetEstadisticas();

    /// <summary>
    /// Devuelve en qué canal está una conexión y cómo se llama.
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <returns>La conexión, o null si esa conexión aún no se ha registrado.</returns>
    ConexionChat? GetConexion(string connectionId);

    /// <summary>
    /// Busca la conexión de una persona DENTRO de un canal (para los mensajes
    /// privados): es el paso inverso a <see cref="GetConexion"/>.
    /// </summary>
    /// <param name="canal">Canal canónico en el que debe estar esa persona.</param>
    /// <param name="usuario">Nombre buscado (se normaliza como en el alta).</param>
    /// <returns>
    /// El connectionId de esa conexión, o null si en ESE canal no hay nadie
    /// con ese nombre. El filtro lleva el canal a propósito: los nombres solo
    /// son únicos dentro de una sala, así que sin el canal el resultado sería
    /// ambiguo. El connectionId no sale nunca del servidor.
    /// </returns>
    string? GetConnectionId(string canal, string usuario);

    /// <summary>
    /// Elimina a un usuario al desconectar, liberando su nombre en su canal.
    /// </summary>
    /// <param name="connectionId">Identificador de la conexión SignalR.</param>
    /// <returns>La conexión que acaba de salir, o null si no estaba registrada.</returns>
    ConexionChat? RemoveUsuario(string connectionId);
}
