using AulaChatNet.Shared.Comandos;

namespace AulaChatNet.Client.Blazor.Components.Services;

/// <summary>
/// Contrato del ejecutor de comandos de la web: recibe un <see cref="Comando"/>
/// ya parseado (el parser vive en Shared y lo comparte con la terminal) y lo
/// ejecuta con los recursos del cliente Blazor — la store, la configuración
/// compartida y la sesión del navegador.
/// </summary>
public interface IProcesadorComandos
{
    /// <summary>
    /// Ejecuta el comando. El resultado se comunica al usuario con avisos
    /// en el flujo de mensajes de la store (regla de la casa: en la caja
    /// solo se pintan los errores de campo; los comandos hablan en el flujo,
    /// igual que la salida de la terminal).
    /// </summary>
    /// <param name="comando">Comando tipado devuelto por ParserComandos.Parse.</param>
    Task EjecutarAsync(Comando comando);
}
