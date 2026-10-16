namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// Tipo de mensaje que circula por el canal.
/// Permite distinguir los mensajes escritos por los usuarios de los
/// mensajes generados por el propio sistema (entradas, salidas...).
/// </summary>
public enum TipoMensaje
{
    /// <summary>Mensaje escrito por un usuario del aula.</summary>
    Normal,

    /// <summary>Mensaje generado por el sistema ("X se ha unido al canal").</summary>
    Sistema
}
