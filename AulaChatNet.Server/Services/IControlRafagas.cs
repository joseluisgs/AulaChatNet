namespace AulaChatNet.Server.Services;

/// <summary>
/// Control de frecuencia de escritura (Fase 4.1): decide si una conexión
/// puede enviar otro mensaje dentro de la ventana de ráfaga configurada.
/// La cuota la pone y aplica SOLO el servidor: el cliente nunca decide.
/// </summary>
public interface IControlRafagas
{
    /// <summary>
    /// Anota un intento de escritura y decide si pasa la cuota.
    /// </summary>
    /// <param name="clave">
    /// Identidad que cotiza: la conexión de SignalR. No el nick, porque los
    /// nicks son únicos solo DENTRO de un canal y dos salas podrían tener
    /// dos "Ana" distintas compartirían cuota por error.
    /// </param>
    /// <param name="ahoraUtc">
    /// Instante del intento. El llamador pasa el reloj a propósito: el
    /// control queda puro y sus pruebas no esperan segundos reales.
    /// </param>
    /// <returns>true si el mensaje pasa; false si la ventana está llena.</returns>
    bool Intentar(string clave, DateTime ahoraUtc);

    /// <summary>
    /// Olvida a una conexión: se llama al desconectar para que el diccionario
    /// de cuotas no crezca sin límite mientras el servidor está en marcha.
    /// </summary>
    /// <param name="clave">Conexión que se fue.</param>
    void Limpiar(string clave);
}
