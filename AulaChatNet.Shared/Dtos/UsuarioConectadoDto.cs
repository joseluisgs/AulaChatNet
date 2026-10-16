namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// DTO de un usuario conectado al canal.
/// Es lo que el servidor envía a los clientes para pintar la lista
/// de usuarios en línea (punto verde en Blazor, panel en terminal).
///
/// <remarks>
/// No incluye el ConnectionId: es un detalle interno de SignalR que
/// cada cliente no necesita conocer.
/// </remarks>
/// </summary>
/// <example>
/// <code>
/// { "usuario": "Ana", "conectadoDesde": "2026-10-15T10:29:00Z" }
/// </code>
/// </example>
public record UsuarioConectadoDto(
    // <summary>
    // Nombre visible del usuario en el canal.
    // Único entre los conectados: el servidor rechaza nombres repetidos.
    // </summary>
    // <example>Ana</example>
    string Usuario,

    // <summary>
    // Fecha y hora (UTC) en la que el usuario se conectó.
    // </summary>
    // <example>2026-10-15T10:29:00Z</example>
    DateTime ConectadoDesde
);
