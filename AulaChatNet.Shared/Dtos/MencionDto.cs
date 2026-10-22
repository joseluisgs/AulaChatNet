namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// Aviso dirigido de mención: llega SOLO a la persona nombrada con @ en un
/// mensaje del canal. El mensaje en sí viaja aparte, por el flujo normal de
/// <see cref="MensajeDto"/>: este DTO no es un mensaje, es un empujoncito
/// para que su cliente pite y parpadee.
///
/// <remarks>
/// Por qué un DTO propio y no un campo en MensajeDto: la mención es un
/// AVISO y no parte del contenido — quien no fue nombrado jamás lo verá,
/// así que no puede vivir en el mensaje que recibe todo el canal. Mismo
/// razonamiento que <see cref="MensajePrivadoDto"/> con los privados:
/// contrato propio para destino propio.
/// </remarks>
/// </summary>
/// <example>
/// Mensaje JSON típico en el cliente:
/// <code>
/// {
///   "emisor": "Luis",
///   "canal": "#general",
///   "texto": "@Ana ¿me pasas el ejercicio 3?",
///   "timestamp": "2026-10-20T17:45:00Z"
/// }
/// </code>
/// </example>
public record MencionDto(
    // <summary>
    // Quien escribió el mensaje que te nombra (insensible a mayúsculas,
    // como todo lo demás del chat).
    // </summary>
    // <example>Luis</example>
    string Emisor,

    // <summary>
    // Canal donde ocurrió: las menciones no cruzan salas, igual que los
    // nicks solo son únicos DENTRO de un canal.
    // </summary>
    // <example>#general</example>
    string Canal,

    // <summary>
    // Texto COMPLETO del mensaje original, para que el cliente pueda
    // mostrar de qué iba la mención sin tener que buscarlo en la vista.
    // </summary>
    // <example>@Ana ¿me pasas el ejercicio 3?</example>
    string Texto,

    // <summary>
    // Instante (UTC) del mensaje original, asignado por el servidor: la
    // misma hora para todos los clientes sin depender del reloj de cada uno.
    // </summary>
    // <example>2026-10-20T17:45:00Z</example>
    DateTime Timestamp
);
