namespace AulaChatNet.Shared.Constantes;

/// <summary>
/// Rutas fijas que comparten servidor y clientes FUERA de SignalR (lo que
/// sería un API REST clásico). Están aquí —y no escritas "a pelo" en cada
/// lado— para que un cambio de nombre no rompa a nadie en silencio.
/// </summary>
public static class ChatRutas
{
    /// <summary>
    /// GET con las estadísticas globales (total conectados y por canal):
    /// los clientes lo usan ANTES de registrarse, para decir cuánta gente
    /// hay en el aula nada más arrancar.
    /// </summary>
    public const string Estadisticas = "/estadisticas";
}
