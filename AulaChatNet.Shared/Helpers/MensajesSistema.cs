using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Fábrica de mensajes de sistema: las notificaciones que genera el propio
/// chat y no un usuario ("X se ha unido al canal", "X ha salido del canal").
///
/// <remarks>
/// Viajan por el mismo canal que los mensajes normales (Tipo = Sistema),
/// así los clientes solo necesitan un único flujo para pintar el historial
/// y las entradas/salidas, y el chat queda como el de Discord.
/// </remarks>
/// </summary>
public static class MensajesSistema
{
    /// <summary>Nombre que aparece como autor de los mensajes de sistema.</summary>
    public const string Autor = "sistema";

    /// <summary>Crea el mensaje "X se ha unido al canal".</summary>
    /// <param name="usuario">Nombre del usuario que acaba de conectarse.</param>
    /// <returns>Mensaje de sistema listo para emitir.</returns>
    public static MensajeDto UsuarioUnido(string usuario) =>
        Crear($"{usuario} se ha unido al canal");

    /// <summary>Crea el mensaje "X ha salido del canal".</summary>
    /// <param name="usuario">Nombre del usuario que se ha desconectado.</param>
    /// <returns>Mensaje de sistema listo para emitir.</returns>
    public static MensajeDto UsuarioSalio(string usuario) =>
        Crear($"{usuario} ha salido del canal");

    private static MensajeDto Crear(string texto) =>
        new(Guid.NewGuid(), Autor, texto, DateTime.UtcNow, TipoMensaje.Sistema);
}
