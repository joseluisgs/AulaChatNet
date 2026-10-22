namespace AulaChatNet.Client.Web.State;

/// <summary>
/// Nick y canal recordados entre recargas (la sesión del navegador, equivalente
/// al <c>sesion.json</c> de la terminal pero en localStorage).
/// </summary>
/// <param name="Nick">Nombre de usuario recordado.</param>
/// <param name="Canal">Canal recordado (puede no estar si la sesión es antigua).</param>
public record SesionGuardada(string Nick, string? Canal);
