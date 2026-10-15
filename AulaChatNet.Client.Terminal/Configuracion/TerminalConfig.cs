namespace AulaChatNet.Client.Terminal.Configuracion;

/// <summary>
/// Configuración propia del cliente terminal (sección "Terminal" del
/// appsettings.json). Lo compartido con el servidor (canales, límites de
/// usuario y mensaje) vive en <see cref="AulaChatNet.Shared.Configuracion.ChatConfig"/>:
/// una sola regla, dos orígenes.
/// </summary>
public class TerminalConfig
{
    /// <summary>Nombre de la sección en appsettings.json.</summary>
    public const string Seccion = "Terminal";

    /// <summary>
    /// Si es true, al salir se guardan nick y canal en el perfil del usuario
    /// (%APPDATA%) y al arrancar se ofrecen como valor por defecto. Es estado
    /// personal, no configuración de la app, por eso el fichero de sesión
    /// vive fuera del repositorio.
    /// </summary>
    public bool RecordarSesion { get; set; } = true;
}
