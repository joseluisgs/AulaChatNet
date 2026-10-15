namespace AulaChatNet.Client.Terminal.Sesion;

/// <summary>
/// Última sesión del usuario en este equipo: el nick y el canal con los que
/// se conectó por última vez, para ofrecerlos como valor por defecto al
/// arrancar (como hace Discord con el último servidor abierto), y cuándo
/// fue, para enseñarlo en la bienvenida.
/// </summary>
/// <param name="Nick">Nombre de usuario usado la última vez.</param>
/// <param name="Canal">Canal en el que estaba la última vez.</param>
/// <param name="UltimaConexion">
/// Cuándo se guardó por última vez. Si el fichero es antiguo y no trae la
/// fecha, queda en <c>default</c> y la bienvenida simplemente no la muestra.
/// </param>
public record SesionTerminal(string Nick, string Canal, DateTime UltimaConexion = default);
