namespace AulaChatNet.Client.Terminal.Sesion;

/// <summary>
/// Última sesión del usuario en este equipo: el nick y el canal con los que
/// se conectó por última vez, para ofrecerlos como valor por defecto al
/// arrancar (como hace Discord con el último servidor abierto).
/// </summary>
/// <param name="Nick">Nombre de usuario usado la última vez.</param>
/// <param name="Canal">Canal en el que estaba la última vez.</param>
public record SesionTerminal(string Nick, string Canal);
