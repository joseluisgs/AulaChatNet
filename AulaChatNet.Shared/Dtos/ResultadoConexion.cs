namespace AulaChatNet.Shared.Dtos;

/// <summary>
/// Resultado de un intento de conexión con el servidor: el error (si lo hay)
/// y, sobre todo, QUÉ falló.
///
/// <remarks>
/// Si <c>ConectarAsync</c> devolviera solo un <c>string</c> con el error, el
/// llamador tendría que adivinar la causa comparando textos — o peor: tratar
/// TODO como "nick ocupado" y volver a preguntar el nick cuando en realidad
/// el servidor ni se alcanzaba (el nick no tiene la culpa). El tipo
/// <c>HayServidor</c> lleva esa distinción, y es ella la que decide el flujo.
/// </remarks>
/// </summary>
/// <param name="Error">null si todo fue correcto; si no, el mensaje para el usuario (ya traducido, nunca un tipo de excepción).</param>
/// <param name="HayServidor">true si el servidor respondió (rechazo de nick/canal); false si ni se pudo alcanzar.</param>
public sealed record ResultadoConexion(string? Error, bool HayServidor)
{
    /// <summary>La conexión salió bien y ya se está dentro.</summary>
    public bool Exitoso => Error is null;
}
