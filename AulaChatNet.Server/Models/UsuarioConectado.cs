namespace AulaChatNet.Server.Models;

/// <summary>
/// Modelo interno de un usuario registrado en el store.
/// Junto a la clave del diccionario (ConnectionId) forma el registro de
/// quién está en el canal en cada momento.
///
/// <remarks>
/// Es interno a propósito: los clientes solo necesitan el
/// <c>UsuarioConectadoDto</c> (sin ConnectionId, que es detalle interno
/// de SignalR).
/// </remarks>
/// </summary>
/// <param name="Usuario">Nombre visible del usuario en el canal.</param>
/// <param name="ConectadoDesde">Momento (UTC) en el que se registró.</param>
internal record UsuarioConectado(string Usuario, DateTime ConectadoDesde);
