using System.Text.Json;

namespace AulaChatNet.Client.Terminal.Sesion;

/// <summary>
/// Guarda y carga la última sesión (nick y canal) en un JSON pequeño dentro
/// del perfil del usuario (%APPDATA%\AulaChatNet\sesion.json en Windows).
///
/// <remarks>
/// Esto NO es configuración de la aplicación: es estado personal del usuario.
/// Por eso vive fuera de appsettings.json (que está en el repo y es común
/// para todo el equipo): cada alumno tiene su fichero en su perfil.
///
/// La ruta se puede inyectar por el constructor: los tests la apuntan a una
/// carpeta temporal y no ensucian el equipo real.
/// </remarks>
/// </summary>
/// <param name="rutaFichero">Ruta del JSON; si se omite, la del perfil del usuario.</param>
public class SesionStore(string? rutaFichero = null)
{
    // Serialize/Deserialize con las mismas opciones en guardar y cargar:
    // si se escriben camelCase, se leen camelCase (el JSON de ejemplo usa
    // PascalCase, pero las dos formas funcionan gracias al CaseInsensitive)
    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true // legible a mano: se puede abrir el fichero y mirarlo
    };

    /// <summary>Fichero real de la sesión (uno por usuario en el equipo).</summary>
    public string RutaFichero { get; } = rutaFichero ?? RutaPorDefecto();

    /// <summary>
    /// Carga la sesión guardada, si existe y es legible.
    /// </summary>
    /// <returns>
    /// La sesión recordada, o null si no hay fichero o está corrupto.
    /// Nunca lanza: un chat no puede romperse por un fichero de sesión malo.
    /// </returns>
    public SesionTerminal? Cargar()
    {
        try
        {
            if (!File.Exists(RutaFichero))
                return null;

            var json = File.ReadAllText(RutaFichero);

            return JsonSerializer.Deserialize<SesionTerminal>(json, Opciones);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // JSON imposible o disco inaccesible: no hay sesión y se vuelve
            // a preguntar; el chat sigue funcionando
            return null;
        }
    }

    /// <summary>
    /// Guarda la sesión actual (sobrescribe la anterior). Crea la carpeta si no existe.
    /// </summary>
    /// <param name="sesion">Nick y canal a recordar para el próximo arranque.</param>
    public void Guardar(SesionTerminal sesion)
    {
        var directorio = Path.GetDirectoryName(RutaFichero);

        if (!string.IsNullOrEmpty(directorio))
            Directory.CreateDirectory(directorio);

        var json = JsonSerializer.Serialize(sesion, Opciones);
        File.WriteAllText(RutaFichero, json);
    }

    /// <summary>
    /// Ruta por defecto en el perfil del usuario: %APPDATA%\AulaChatNet\sesion.json
    /// en Windows, o el equivalente (XDG) en Linux y macOS.
    /// </summary>
    /// <returns>Ruta completa del fichero de sesión.</returns>
    private static string RutaPorDefecto() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AulaChatNet",
            "sesion.json");
}
