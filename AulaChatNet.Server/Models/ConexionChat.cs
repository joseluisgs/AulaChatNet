namespace AulaChatNet.Server.Models;

/// <summary>
/// Estado público de una conexión registrada: en qué canal está y cómo se
/// llama. Es lo que el store devuelve al hub para que sepa a qué grupo de
/// SignalR emitir.
/// </summary>
/// <param name="Canal">Canal canónico (forma de la configuración, p. ej. "#smr").</param>
/// <param name="Usuario">Nombre visible del usuario.</param>
public record ConexionChat(string Canal, string Usuario);
