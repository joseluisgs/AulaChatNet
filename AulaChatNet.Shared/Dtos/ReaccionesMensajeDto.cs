namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// Estado COMPLETO de las reacciones de UN mensaje: la lista de chips que
/// deben pintarse bajo él.
///
/// <remarks>
/// Se manda el estado ENTERO y no un "alguien añadió X": así el cliente solo
/// REEMPLAZA su entrada por mensaje sin reconstruir nada — es idempotente y
/// no puede quedar descuadrado aunque se crucen varios envíos.
/// Una lista de reacciones vacía significa "este mensaje ya no tiene
/// ninguna" (alguien quitó la última); el cliente entonces borra la fila.
/// </remarks>
/// </summary>
/// <example>
/// JSON típico en el cliente:
/// <code>
/// {
///   "idMensaje": "3f8b0c2e-9a4d-4c1b-8f2e-1d5a6b7c8d9e",
///   "reacciones": [ { "emoji": "👍", "personas": ["Ana", "Bruno"] } ]
/// }
/// </code>
/// </example>
public record ReaccionesMensajeDto(Guid IdMensaje, IReadOnlyList<ReaccionDto> Reacciones);
