namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// DTO con las estadísticas globales de conexión del chat: cuánta gente hay
/// conectada EN TOTAL y cuántas hay en cada sala.
///
/// <remarks>
/// El servidor lo emite con el evento <c>EstadisticasActualizadas</c> a TODOS
/// los clientes cada vez que alguien entra, sale o cambia de sala; así cada
/// interfaz puede mostrar los contadores en todo momento (línea de estado de
/// la terminal, cabecera de Blazor...) sin tener que preguntar.
/// </remarks>
/// </summary>
/// <example>
/// <code>
/// {
///   "totalConectados": 12,
///   "canales": [
///     { "canal": "#general", "conectados": 3 },
///     { "canal": "#smr", "conectados": 4 }
///   ]
/// }
/// </code>
/// </example>
public record EstadisticasDto(
    // <summary>Total de personas conectadas en TODOS los canales.</summary>
    // <example>12</example>
    int TotalConectados,

    // <summary>
    // Desglose por canal, del más poblado al menos. Solo aparecen los
    // canales con alguien dentro: los vacíos no se emiten.
    // </summary>
    IReadOnlyList<ConteoCanalDto> Canales
);
