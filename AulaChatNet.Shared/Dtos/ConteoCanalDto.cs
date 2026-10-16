namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// DTO con el número de usuarios conectados en UN canal (sala) ahora mismo.
/// </summary>
/// <example>
/// { "canal": "#general", "conectados": 3 }
/// </example>
public record ConteoCanalDto(
    // <summary>Canal canónico tal y como aparece en la configuración.</summary>
    // <example>#general</example>
    string Canal,

    // <summary>Cuántas personas hay dentro de esa sala en este momento.</summary>
    // <example>3</example>
    int Conectados
);
