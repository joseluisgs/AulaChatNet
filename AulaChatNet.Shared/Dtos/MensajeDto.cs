namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// DTO (Data Transfer Object) de un mensaje del chat.
/// Este objeto se utiliza para transferir mensajes desde el servidor hacia
/// todos los clientes conectados (terminal y Blazor).
///
/// Es inmutable (record) para garantizar la integridad de los datos en tránsito:
/// una vez recibido, un mensaje no puede cambiar.
///
/// <remarks>
/// El avatar NO viaja en el DTO: cada cliente lo deriva del nombre de usuario
/// con <c>AvatarHelper</c> (DiceBear en web, iniciales en terminal).
/// Así el servidor no conoce detalles de presentación de cada interfaz.
/// </remarks>
/// </summary>
/// <example>
/// Mensaje JSON típico en el cliente:
/// <code>
/// {
///   "id": "3f8b0c2e-...",
///   "usuario": "Ana",
///   "texto": "Buenas, ¿arrancamos?",
///   "timestamp": "2026-10-15T10:30:00Z",
///   "tipo": 0,
///   "editado": false
/// }
/// </code>
/// </example>
public record MensajeDto(
    // <summary>
    // Identificador único del mensaje (GUID).
    // Se genera en el servidor al recibir el mensaje; sirve para
    // distinguir mensajes repetidos y ordenarlos en la interfaz.
    // </summary>
    // <example>3f8b0c2e-9a4d-4c1b-8f2e-1d5a6b7c8d9e</example>
    Guid Id,

    // <summary>
    // Nombre de usuario que escribió el mensaje.
    // En los mensajes de sistema contiene el usuario afectado
    // o el texto del aviso.
    // </summary>
    // <example>Ana</example>
    string Usuario,

    // <summary>
    // Contenido del mensaje escrito por el usuario.
    // Vacío en los mensajes de sistema (el texto va en Texto igualmente).
    // Límite de longitud controlado por ChatConfig.MensajeLongitudMaxima (appsettings).
    // </summary>
    // <example>Buenas, ¿arrancamos?</example>
    string Texto,

    // <summary>
    // Fecha y hora del mensaje en UTC.
    // La asigna el servidor para que todos los clientes
    // vean la misma hora, sin depender del reloj de cada uno.
    // </summary>
    // <example>2026-10-15T10:30:00Z</example>
    DateTime Timestamp,

    // <summary>
    // Tipo del mensaje: Normal (de usuario) o Sistema (unión/salida).
    // </summary>
    TipoMensaje Tipo,

    // <summary>
    // true si el texto se ha modificado DESPUÉS de enviarlo (Fase 4.4).
    // Los clientes lo pintan como "(editado)".
    // El Timestamp NO cambia al editar: sigue siendo el instante original
    // de envío, que es el cursor con el que se pagina el historial.
    // </summary>
    // <example>false</example>
    bool Editado = false
);
