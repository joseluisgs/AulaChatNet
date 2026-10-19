namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// Un chip de reacción: UN emoji con las personas que lo pusieron sobre un
/// mensaje.
///
/// <remarks>
/// Inmutable (record) como todo lo que viaja por la red. Las personas van
/// ordenadas alfabéticamente y con el nombre TAL COMO se registraron en ese
/// momento: cambiar de nick después no reescribe las reacciones viejas, y
/// disimularlo sería mentir — sin autenticación no hay identidad estable a
/// la que reescribir.
/// </remarks>
/// </summary>
/// <example>
/// JSON típico en el cliente:
/// <code>
/// { "emoji": "👍", "personas": ["Ana", "Bruno"] }
/// </code>
/// </example>
public record ReaccionDto(string Emoji, IReadOnlyList<string> Personas);
