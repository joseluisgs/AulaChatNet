namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// DTO de un mensaje PRIVADO entre dos personas del mismo canal.
///
/// <remarks>
/// <para>
/// Se separa de <see cref="MensajeDto"/> a propósito, y la separación es la
/// lección de esta pieza:
/// </para>
/// <list type="bullet">
/// <item><b>Destino distinto</b>: un mensaje del canal va a <c>Group</c>, este
/// va a DOS conexiones concretas. Ese destino se resuelve en el servidor y no
/// necesita ir dentro del cuerpo del mensaje.</item>
/// <item><b>Vida distinta</b>: los privados NO entran en el historial de canal
/// (si entraran, quien se conectara después se los encontraría) y el servidor
/// NO los guarda en base de datos: sin autenticación, el siguiente que use ese
/// nombre se los llevaría tras un reinicio.</item>
/// </list>
/// Por eso existe un DTO propio en lugar de un campo en <see cref="MensajeDto"/>:
/// así el contrato de la Fase 2 sigue intacto y la confusión entre "lo que ve
/// todo el canal" y "lo que ven dos personas" es imposible por construcción.
/// </remarks>
/// </summary>
/// <example>
/// Mensaje JSON típico en el cliente:
/// <code>
/// {
///   "id": "9a1c4f77-...",
///   "emisor": "Ana",
///   "destinatario": "Luis",
///   "texto": "¿Nos vemos en el recreo?",
///   "timestamp": "2026-10-19T10:30:00Z"
/// }
/// </code>
/// </example>
public record MensajePrivadoDto(
    // <summary>
    // Identificador único del mensaje (GUID), generado en el servidor.
    // Sirve de clave estable para repintar la lista en Blazor.
    // </summary>
    // <example>9a1c4f77-8b2e-4d1a-9c3f-1e5d7a2b4c6e</example>
    Guid Id,

    // <summary>
    // Quien lo escribe. Si coincide con tu nombre, tú lo enviaste
    // (la vista lo pintará como "para X"); si no, te lo han enviado
    // (la vista lo pintará como "de X").
    // </summary>
    // <example>Ana</example>
    string Emisor,

    // <summary>
    // A quién va dirigido. Siempre dentro del canal del emisor: los privados
    // no cruzan salas, porque los nombres solo son únicos DENTRO de un canal.
    // </summary>
    // <example>Luis</example>
    string Destinatario,

    // <summary>
    // Contenido del mensaje. Misma validación de longitud que un mensaje
    // de canal (ChatConfig.ValidarMensaje), aplicada en el hub.
    // </summary>
    // <example>¿Nos vemos en el recreo?</example>
    string Texto,

    // <summary>
    // Fecha y hora del mensaje en UTC, asignada por el servidor: todos los
    // clientes ven la misma hora sin depender del reloj de cada uno.
    // </summary>
    // <example>2026-10-19T10:30:00Z</example>
    DateTime Timestamp
);
