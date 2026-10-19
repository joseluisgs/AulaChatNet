using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AulaChatNet.Shared.Helpers;

/// <summary>
/// Quién está escribiendo... AHORA MISMO: recibe las señales de escritura
/// que llegan del servidor, las mantiene vivas mientras se renueven y las
/// descarta SOLAS cuando llevan demasiado tiempo sin aparecer.
///
/// <remarks>
/// Tres ideas que conviene leer juntas:
/// <list type="bullet">
/// <item>Este control vive en Shared (no en cada cliente) para que la
/// terminal y la web se comporten IGUAL —misma caducidad, mismo orden,
/// mismo texto— y para probarlo UNA sola vez.</item>
/// <item>El servidor no guarda nada de esto: solo reenvía el nombre de
/// quien escribe. Si alguien se desconecta a mitad de una frase, su marca
/// se apaga sola en <see cref="CaducidadPorDefecto"/>, sin estado que
/// limpiar en nadie. Mismo criterio efímero que los mensajes privados.</item>
/// <item>El reloj es un <c>IScheduler</c> de Rx: en producción marca el de
/// la máquina y en los tests un reloj VIRTUAL (TestScheduler) que avanza el
/// tiempo sin esperar ni un milisegundo real.</item>
/// </list>
///
/// Se toca desde DOS hilos —los eventos de SignalR y el latido del reloj—,
/// por eso el candado: un Dictionary no admite dos escrituras a la vez. La
/// emisión del observable se hace TAMBIÉN dentro del candado, para que los
/// suscriptores reciban los cambios en el MISMO orden en que ocurrieron.
/// </remarks>
/// </summary>
public sealed class SeguimientoEscritura : IDisposable
{
    /// <summary>Cuánto vive una señal sin renovarse: 5 segundos.</summary>
    public static readonly TimeSpan CaducidadPorDefecto = TimeSpan.FromSeconds(5);

    /// <summary>Cada cuánto se revisa quién ha caducado: 1 segundo.</summary>
    public static readonly TimeSpan PeriodoRevision = TimeSpan.FromSeconds(1);

    // Último instante en que cada persona señaló, por nombre. Sin mayúsculas
    // distintas: en un canal dos personas no pueden llamarse "Ana" y "ana"
    private readonly Dictionary<string, DateTime> _ultimaSenal = new(StringComparer.OrdinalIgnoreCase);

    private readonly BehaviorSubject<IReadOnlyList<string>> _activos = new([]);
    private readonly IScheduler _reloj;
    private readonly TimeSpan _caducidad;
    private readonly object _candado = new();
    private readonly IDisposable _latido;

    /// <summary>
    /// Crea el seguimiento y arranca el latido que descarta a los caducados.
    /// </summary>
    /// <param name="reloj">Reloj de Rx (por defecto el de la máquina; los tests pasan uno virtual).</param>
    /// <param name="caducidad">Cuánto dura una señal sin renovarse (por defecto <see cref="CaducidadPorDefecto"/>).</param>
    public SeguimientoEscritura(IScheduler? reloj = null, TimeSpan? caducidad = null)
    {
        _reloj = reloj ?? Scheduler.Default;
        _caducidad = caducidad ?? CaducidadPorDefecto;

        // El latido: cada periodo se mira quién lleva más de la caducidad sin
        // señalar. Con un reloj virtual, los tests llaman a AdvanceBy(...) y
        // el tiempo pasa al instante, sin esperas ni hilos sueltos
        _latido = Observable
            .Interval(PeriodoRevision, _reloj)
            .Subscribe(_ => Caducar());
    }

    /// <summary>Personas del canal que están escribiendo, en orden alfabético.</summary>
    public IObservable<IReadOnlyList<string>> Activos => _activos;

    /// <summary>
    /// Último valor de la lista, para leerlo sin suscribirse: así lo repinta
    /// la TUI de la terminal en su bucle de refresco.
    /// </summary>
    public IReadOnlyList<string> Vista => _activos.Value;

    /// <summary>
    /// Activa (o renueva) la señal de alguien. Si con esto la lista cambia —
    /// alguien nuevo, o alguien que había caducado mientras tanto— se emite
    /// el valor nuevo; si no cambia, no se emite nada (renovar no es noticia).
    /// </summary>
    /// <param name="usuario">Nombre de quien escribe.</param>
    public void Senalar(string usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            return;

        var clave = usuario.Trim();

        lock (_candado)
        {
            var ahora = _reloj.Now.UtcDateTime;

            // Tocar la lista aprovecha para descartar a los caducados
            var caducados = DescartarCaducados(ahora);
            var eraNuevo = !_ultimaSenal.ContainsKey(clave);
            _ultimaSenal[clave] = ahora;

            if (caducados > 0 || eraNuevo)
                _activos.OnNext(Ordenados());
        }
    }

    /// <summary>
    /// Apaga la señal de alguien al instante: lo usa el cliente cuando le
    /// llega un SUYO mensaje — quien acaba de hablar ya no está escribiendo,
    /// y esperar a la caducidad sería un parpadeo innecesario.
    /// </summary>
    /// <param name="usuario">Nombre de quien ya no escribe.</param>
    public void Quitar(string usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            return;

        lock (_candado)
        {
            if (_ultimaSenal.Remove(usuario.Trim()))
                _activos.OnNext(Ordenados());
        }
    }

    /// <summary>
    /// Vacía todo: cambio de canal o cierre de sesión. Lo que había en la
    /// sala anterior no debe asomar en la nueva ni un instante.
    /// </summary>
    public void Limpiar()
    {
        lock (_candado)
        {
            if (_ultimaSenal.Count == 0)
                return;

            _ultimaSenal.Clear();
            _activos.OnNext(Ordenados());
        }
    }

    /// <summary>
    /// Latido del reloj: descarta a los que llevan la caducidad o más sin
    /// señalar y, solo si alguien se ha ido, emite la lista nueva.
    /// </summary>
    private void Caducar()
    {
        lock (_candado)
        {
            // Con la lista vacía no hay nada que mirar (y es el caso
            // normal: el latido corre toda la vida de la store)
            if (_ultimaSenal.Count == 0)
                return;

            if (DescartarCaducados(_reloj.Now.UtcDateTime) > 0)
                _activos.OnNext(Ordenados());
        }
    }

    /// <summary>
    /// Descarta a los que llevan la caducidad o más sin señalar.
    /// Llamar SIEMPRE con el candado tomado.
    /// </summary>
    /// <param name="ahora">Instante contra el que se compara.</param>
    /// <returns>Cuántos se han descartado.</returns>
    private int DescartarCaducados(DateTime ahora)
    {
        // No se puede borrar de un Dictionary mientras se recorre, así que
        // primero se juntan los caducados y después se quitan
        var caducados = _ultimaSenal
            .Where(par => ahora - par.Value >= _caducidad)
            .Select(par => par.Key)
            .ToList();

        foreach (var clave in caducados)
            _ultimaSenal.Remove(clave);

        return caducados.Count;
    }

    /// <summary>
    /// Foto de los activos en orden alfabético. El orden importa: si fuera
    /// el de llegada, los nombres bailarían cada vez que alguien renueva.
    /// Llamar SIEMPRE con el candado tomado.
    /// </summary>
    private IReadOnlyList<string> Ordenados() =>
        [.. _ultimaSenal.Keys.OrderBy(clave => clave, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Detiene el latido y libera el observable.</summary>
    public void Dispose()
    {
        _latido.Dispose();
        _activos.Dispose();
    }
}
