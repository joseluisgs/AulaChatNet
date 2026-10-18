using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Entidad de persistencia de un mensaje: lo que la base de datos guarda en la
/// tabla <c>Mensajes</c>. Es la Fase 2 del historial, que en la Fase 1 vivía en
/// una <c>ConcurrentQueue</c> y moría al parar el servidor.
///
/// <remarks>
/// <para>
/// <b>Por qué no es el <see cref="MensajeDto"/>:</b> las capas se separan a
/// propósito. El DTO viaja por la red hacia los clientes; la entidad es el
/// reflejo del esquema en disco. Que sean dos objetos permite cambiar uno sin
/// tocar al otro (partir la tabla en dos, añadir una columna de auditoría,
/// cambiar el motor...) sin que el contrato con los clientes se mueva.
/// </para>
/// <para>
/// <b>El canal va como texto denormalizado</b>, sin clave foránea: los canales
/// los define la configuración (appsettings.json), no los datos. Meter una
/// tabla <c>Canales</c> solo para poder hacer un JOIN innecesario sería
/// complejidad sin valor.
/// </para>
/// <para>
/// Es una clase mutable (no un <c>record</c>) porque EF Core la rellena
/// propiedad a propiedad al leer de disco y necesita un constructor sin
/// argumentos y setters. Los <c>record</c> son para los DTOs inmutables; aquí
/// lo que importa es que el ORM pueda materializarla.
/// </para>
/// </remarks>
/// </summary>
public class MensajeEntidad
{
    /// <summary>
    /// Identificador único del mensaje. Es el MISMO <see cref="MensajeDto.Id"/>
    /// que genera el servidor al recibirlo, no uno nuevo de la base de datos:
    /// así el cliente puede reconocer "su" mensaje sin id adicional.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>Canal canónico donde se escribió (p. ej. "#general").</summary>
    public string Canal { get; set; } = string.Empty;

    /// <summary>Nick de quien escribió el mensaje.</summary>
    /// <remarks>
    /// Va como texto, sin tabla de usuarios: en este proyecto no existen las
    /// cuentas (no hay registro ni contraseña), así que no hay identidad que
    /// enlazar. Un límite honesto: si dos personas usan el mismo nick en
    /// momentos distintos, no se pueden distinguir.
    /// </remarks>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>Contenido del mensaje. Vacío en los mensajes de sistema.</summary>
    public string Texto { get; set; } = string.Empty;

    /// <summary>
    /// Momento (UTC) en el que se escribió, asignado SIEMPRE por el servidor.
    ///
    /// <remarks>
    /// Es <c>DateTimeOffset</c> y no <c>DateTime</c> a propósito: SQLite no
    /// tiene un tipo de fecha nativo, guarda texto, y el proveedor de EF Core
    /// puede DEVOLVER un <c>DateTime</c> con el <c>Kind</c> perdido (Unspecified
    /// en vez de Utc). Con <c>DateTimeOffset</c> el desfase viaja en el propio
    /// valor y el redondeo es siempre correcto. Es el gotcha número uno de
    /// SQLite con fechas.
    /// </remarks>
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Tipo del mensaje: <see cref="TipoMensaje.Normal"/> (de usuario) o
    /// <see cref="TipoMensaje.Sistema"/> (unión, salida, cambio de nick).
    /// </summary>
    /// <remarks>
    /// Se guarda como entero (0 y 1), que es como EF Core mapea los enums por
    /// defecto. Aunque ahora SOLO se persisten los normales, la columna
    /// contempla ambos valores por si algún día cambia la decisión.
    /// </remarks>
    public TipoMensaje Tipo { get; set; }
}
