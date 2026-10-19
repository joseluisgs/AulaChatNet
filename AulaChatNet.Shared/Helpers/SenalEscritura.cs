using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Convierte las teclas del editor en SEÑALES de "estoy escribiendo" al
/// servidor, como mucho una vez por periodo: el operador <c>Sample</c> de Rx
/// se queda con el ÚLTIMO valor de cada ventana y lo emite una sola vez.
///
/// <remarks>
/// Por qué <c>Sample</c> y no <c>Throttle</c>/<c>Debounce</c>, que es el
/// error clásico de este diseño: Throttle/Debounce ESPERA a que dejen de
/// llegar eventos, de modo que mientras la persona teclea seguido NO se emite
/// NADA y el indicador solo aparecería cuando ya se ha dejado de escribir,
/// que es lo contrario de lo que se quiere. Sample, en cambio, manda una
/// señal por ventana mientras sigan llegando teclas, y en cuanto se paran, se
/// para solo.
///
/// Dentro de cada ventana se emite el último valor. Si ese último valor es un
/// envío (<see cref="Envio"/>), no se manda nada: sin eso, el mensaje saldría
/// a la pantalla ajena y la señal de la ventana en curso llegaría un instante
/// DESPUÉS, reponiendo al autor en la lista como "sigue escribiendo" cuando
/// ya se había mandado todo.
///
/// La primera señal sale, como mucho, un periodo después de la primera tecla;
/// después, una por periodo mientras se siga escribiendo.
/// </remarks>
/// </summary>
public sealed class SenalEscritura : IDisposable
{
    /// <summary>Periodo por defecto entre señales: 1 segundo.</summary>
    public static readonly TimeSpan PeriodoPorDefecto = TimeSpan.FromSeconds(1);

    private readonly Subject<bool> _teclas = new();
    private readonly IDisposable _muestreo;

    /// <summary>
    /// Crea el muestreo y lo deja suscrito; se libera con <see cref="Dispose"/>.
    /// </summary>
    /// <param name="enviar">
    /// Qué hacer cuando toca mandar la señal. Se lanza EN SEGUNDO PLANO
    /// (fire &amp; forget), así que el método nunca debe lanzar excepción.
    /// </param>
    /// <param name="reloj">Reloj de Rx (por defecto el de la máquina; los tests usan uno virtual).</param>
    /// <param name="periodo">Cuánto dura cada ventana de muestreo (por defecto <see cref="PeriodoPorDefecto"/>).</param>
    public SenalEscritura(Func<Task> enviar, IScheduler? reloj = null, TimeSpan? periodo = null)
    {
        _muestreo = _teclas
            .Sample(periodo ?? PeriodoPorDefecto, reloj ?? Scheduler.Default)
            .Subscribe(sigueEscribiendo =>
            {
                if (sigueEscribiendo)
                    _ = enviar();
            });
    }

    /// <summary>Nueva tecla (o texto pegado): el usuario sigue escribiendo.</summary>
    public void Tecla() => _teclas.OnNext(true);

    /// <summary>Se acaba de mandar un mensaje: corta la señal de la ventana en curso.</summary>
    public void Envio() => _teclas.OnNext(false);

    /// <summary>Libera la suscripción de muestreo y el sujeto.</summary>
    public void Dispose()
    {
        _muestreo.Dispose();
        _teclas.Dispose();
    }
}
