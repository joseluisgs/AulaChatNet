using System.Collections.Concurrent;
using AulaChatNet.Shared.Configuracion;
using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Services;

/// <summary>
/// Ventana deslizante de ráfagas por conexión: en memoria, sin disco y sin
/// estado global compartido entre aulas.
///
/// <remarks>
/// <b>Tres decisiones que conviene entender, no copiar:</b>
/// <list type="bullet">
/// <item><b>El estado vive en un singleton</b> porque los hubs de SignalR
/// son transitorios: se crea una instancia NUEVA en cada invocación, así
/// que la cuota no puede guardarse en el hub (se perdería entre mensajes).</item>
/// <item><b>La clave es la conexión</b> (y no el nick): es única, no hay
/// colisiones entre homónimos de canales distintos, y al desconectar se
/// borra con ella la cuota.</item>
/// <item><b>El reloj lo pasa quien llama</b>: con <c>DateTime.UtcNow</c> en
/// producción y un instante fijo en las pruebas, que prueban la ventana
/// entera sin esperar ni un segundo real.</item>
/// </list>
/// </remarks>
/// </summary>
/// <example>
/// <code>
/// var control = new ControlRafagas(Options.Create(new ChatConfig()));
/// control.Intentar("conn-1", t0); // → true  (1 de 5)
/// control.Intentar("conn-1", t0); // → true  (2 de 5)
/// // ... y el sexto dentro de la ventana → false
/// </code>
/// </example>
public sealed class ControlRafagas(IOptions<ChatConfig> opciones) : IControlRafagas
{
    // Ventana por conexión: cada clave tiene SU cola de instantes
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _ventanas = new();

    /// <inheritdoc />
    public bool Intentar(string clave, DateTime ahoraUtc)
    {
        var config = opciones.Value;

        // 0 o menos = sin límite (mismo criterio que RetencionDias: el
        // valor apagado vive en la config, no en un if del llamador)
        if (config.RafagaMensajes <= 0)
            return true;

        var marcas = _ventanas.GetOrAdd(clave, _ => new Queue<DateTime>());
        var ventana = TimeSpan.FromSeconds(config.RafagaVentanaSegundos);

        // La Queue NO es thread-safe y dos invocaciones de la MISMA
        // conexión pueden solaparse: candado POR CONEXIÓN, no global,
        // para que un aula llena no frene a todas las demás
        lock (marcas)
        {
            // Ventana deslizante: fuera de la cola todo lo que ya caducó
            while (marcas.Count > 0 && ahoraUtc - marcas.Peek() >= ventana)
                marcas.Dequeue();

            // Cuota llena: el intento se cae y el hub devolverá el aviso
            if (marcas.Count >= config.RafagaMensajes)
                return false;

            marcas.Enqueue(ahoraUtc);
            return true;
        }
    }

    /// <inheritdoc />
    public void Limpiar(string clave) => _ventanas.TryRemove(clave, out _);
}
