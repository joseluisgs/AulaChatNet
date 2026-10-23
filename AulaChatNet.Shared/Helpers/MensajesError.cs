using System.Net.Sockets;
using System.Text.Json;

namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Traduce excepciones técnicas a mensajes que ENTienda quien usa el chat.
///
/// <remarks>
/// Convención del proyecto: en pantalla <b>nunca</b> aparece el tipo de una
/// excepción — "HttpRequestException" no le dice nada a quien solo quiere
/// entrar al aula. El tipo es un detalle para nosotras: aquí se traduce a
/// dos preguntas que sí se entienden, <c>¿qué pasa?</c> y <c>qué puedo
/// hacer?</c>. El detalle técnico completo (tipo y mensaje original) no va
/// a la consola del usuario: se escribe en el log de cada cliente (Serilog),
/// que es donde de verdad sirve para diagnosticar.
///
/// Es lógica pura: sin consola, sin red, sin estado — por eso
/// <c>MensajesErrorTests</c> la prueba en milisegundos.
/// </remarks>
/// </summary>
public static class MensajesError
{
    /// <summary>
    /// Devuelve la causa del fallo en palabras de usuario, para la excepción dada.
    /// </summary>
    /// <param name="ex">Excepción capturada (nunca se muestra tal cual).</param>
    /// <returns>Frase en español: qué pasa y qué se puede hacer.</returns>
    public static string DescripcionDe(Exception ex) => ex switch
    {
        // No hay nada detrás de la URL (servidor apagado, puerto mal...)
        HttpRequestException => "no responde en esa dirección (¿está arrancado y en la URL correcta?)",

        // Timeout: la conexión salió pero nadie contestó a tiempo
        OperationCanceledException or TimeoutException =>
            "tardó demasiado en responder (¿está el servidor colgado o sobrecargado?)",

        // Ni siquiera hay red o no se encuentra el host remoto
        SocketException => "no hay red o no se encuentra el equipo del servidor",

        // Respuesta inesperada: no es el AulaChatNet que esperábamos
        JsonException => "respondió con datos que no se esperaban (¿es la URL correcta?)",

        // Cualquier otra: mensaje honesto, sin asustar ni filtrar tipos
        _ => "falló por un motivo inesperado (vuelve a intentarlo; si insiste, mira la consola del servidor)"
    };
}
