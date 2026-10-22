namespace AulaChatNet.Client.Blazor.Components.State;

/// <summary>
/// Guarda y carga la última sesión (nick y canal) en el navegador.
///
/// <remarks>
/// Esto NO es configuración de la aplicación: es estado personal del
/// usuario. Por eso vive en localStorage y no en appsettings.json (que está
/// en el repo y es común para todo el equipo). En aulas con equipos
/// compartidos se puede borrar desde el propio panel de entrada.
/// </remarks>
/// </summary>
public interface ISesionStore
{
    /// <summary>
    /// Carga la sesión guardada, si la hay y es legible.
    /// </summary>
    /// <returns>La sesión recordada, o null si no hay nada o no se pudo leer.</returns>
    Task<SesionGuardada?> CargarAsync();

    /// <summary>Guarda la sesión actual (sobrescribe la anterior).</summary>
    /// <param name="sesion">Nick y canal a recordar para la próxima recarga.</param>
    Task GuardarAsync(SesionGuardada sesion);

    /// <summary>
    /// Borra la sesión guardada, si la hay (útil en aulas compartidas para no
    /// heredar el nick del compañero). No falla nunca.
    /// </summary>
    /// <returns>true si había algo guardado y se borró; false si no había nada.</returns>
    Task<bool> BorrarAsync();
}
