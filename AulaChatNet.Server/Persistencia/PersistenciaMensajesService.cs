using System.Threading.Channels;
using AulaChatNet.Server.Services;
using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Servicio en segundo plano que vacía la cola de escritura del <see cref="ChatStore"/>
/// y persiste los mensajes en la base de datos (Fase 2).
/// </summary>
/// <remarks>
/// <para>
/// Es un <c>BackgroundService</c>: su <c>ExecuteAsync</c> corre en un hilo propio
/// SIN bloquear el hilo de SignalR. El chat sigue respondiendo al instante porque
/// <c>ChatStore.AddMensaje</c> solo encola; la escritura REAL a disco ocurre aquí,
/// en paralelo. Esa es la clave de que la persistencia no ralentice el chat.
/// </para>
/// <para>
/// Si un guardado falla, se loguea y SE SIGUE: un error de disco no debe tumbar
/// la cola ni al servidor. En el apagado se intenta escribir lo que quede
/// pendiente durante un margen corto para no perder los últimos mensajes.
/// </para>
/// </remarks>
public class PersistenciaMensajesService(
    ChatStore store,
    IMensajesRepository repository,
    ILogger<PersistenciaMensajesService> logger)
    : BackgroundService
{
    /// <summary>Margen máximo, tras el apagado, para escribir lo que quede encolado.</summary>
    private static readonly TimeSpan MargenApagado = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lector = store.ColaEscritura;

        logger.LogInformation("🖊️ Cola de persistencia activa: los mensajes se guardan en segundo plano");

        // Bucle principal: espera a que haya algo encolado y lo vacía.
        try
        {
            while (await lector.WaitToReadAsync(stoppingToken))
                await DrenarAsync(lector, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Apagado normal: el host cancela stoppingToken y se sale del bucle.
        }

        // Drenaje final con margen corto y token PROPIO: al cancelarse
        // stoppingToken, lo que aún esté en la cola se intenta escribir igual,
        // para no perder los últimos mensajes. El token propio evita que un disco
        // lento o colgado retenga el apagado del servidor.
        using var margen = new CancellationTokenSource(MargenApagado);
        try
        {
            await DrenarAsync(lector, margen.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("⏱️ Se agotó el margen de apagado; puede haber mensajes sin persistir");
        }

        logger.LogInformation("🖊️ Cola de persistencia detenida");
    }

    /// <summary>
    /// Escribe en la base de datos todo lo que haya encolado en el lector dado.
    /// </summary>
    /// <param name="lector">Lector de la cola de escritura.</param>
    /// <param name="cancelacion">Token que corta el drenado (apagado o margen).</param>
    private async Task DrenarAsync(
        ChannelReader<(string Canal, MensajeDto Mensaje)> lector, CancellationToken cancelacion)
    {
        while (lector.TryRead(out var item))
        {
            try
            {
                await repository.GuardarAsync(item.Canal, item.Mensaje, cancelacion);
            }
            catch (OperationCanceledException)
            {
                // Se propaga para que el llamador sepa que el drenado se cortó
                throw;
            }
            catch (Exception ex)
            {
                // Un fallo al guardar NUNCA rompe la cola: se registra y se sigue
                logger.LogError(ex,
                    "No se pudo persistir el mensaje {Id} del canal {Canal}",
                    item.Mensaje.Id, item.Canal);
            }
        }
    }
}
