using Microsoft.Extensions.Options;

namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Servicio en segundo plano que mantiene el historial de la base de datos:
/// borra los mensajes que superan la retención y compacta el fichero cuando
/// queda muy fragmentado (Fase 2).
/// </summary>
/// <remarks>
/// <para>
/// Es un <c>BackgroundService</c>: su <c>ExecuteAsync</c> corre en un hilo
/// propio SIN bloquear el hilo de SignalR. Limpiar la base de datos no
/// retrasa ni un milisegundo a los usuarios conectados.
/// </para>
/// <para>
/// <b>Retención por TIEMPO, no por cantidad.</b> Se configura cuántos días se
/// conserva todo (<c>RetencionDias</c>), y cada <c>LimpiezaCadaHoras</c> se
/// borra lo que sea más viejo. Es más fácil de razonar que limitar el número
/// de mensajes: "me quedo con un mes" se entiende sin pensar.
/// </para>
/// <para>
/// <b>El VACUUM es opcional y va por separado del DELETE</b>, porque hacen
/// cosas distintas: el <c>DELETE</c> marca páginas como libres (SQLite las
/// reutiliza, pero no las suelta al sistema operativo) y el <c>VACUUM</c>
/// reescribe el fichero compacto devolviendo de verdad el espacio. Por eso el
/// <c>VACUUM</c> es caro y solo se ejecuta si la fragmentación supera el
/// umbral <c>VacuumSiFragmentado</c> (0 = nunca).
/// </para>
/// </remarks>
/// <example>
/// appsettings.json:
/// <code>
/// {
///   "Persistencia": {
///     "LimpiezaCadaHoras": 24,
///     "RetencionDias": 90,
///     "VacuumSiFragmentado": 25
///   }
/// }
/// </code>
/// </example>
public class LimpiezaProgramada(
    IMensajesRepository repository,
    IOptions<PersistenciaConfig> opciones,
    ILogger<LimpiezaProgramada> logger)
    : BackgroundService
{
    /// <summary>Configuración vigente (se relee en cada pasada por si se recarga).</summary>
    private PersistenciaConfig Config => opciones.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 0 horas = limpieza automática apagada. Es la forma de desactivar el
        // servicio sin tocar código (útil para pruebas o para un despliegue
        // donde se prefiere no tocar la base de datos en caliente).
        if (Config.LimpiezaCadaHoras <= 0)
        {
            logger.LogInformation("🧹 Limpieza automática desactivada (LimpiezaCadaHoras = 0)");
            return;
        }

        // Sin retención no se borra nada, y sin umbral de VACUUM no se
        // compacta: si ambas cosas están apagadas, el servicio no tiene nada
        // que hacer y no merece un hilo durmiendo.
        if (Config.RetencionDias <= 0 && Config.VacuumSiFragmentado <= 0)
        {
            logger.LogInformation("🧹 Limpieza automática sin efecto: retención ilimitada y VACUUM desactivado");
            return;
        }

        var intervalo = TimeSpan.FromHours(Config.LimpiezaCadaHoras);
        var vacuum = Config.VacuumSiFragmentado > 0
            ? $"VACUUM al {Config.VacuumSiFragmentado}% de fragmentación"
            : "VACUUM desactivado";

        // Se loguean las HORAS tal cual (24) y no el TimeSpan (que saldría
        // "1.00:00:00" y solo confunde al lector).
        logger.LogInformation(
            "🧹 Limpieza programada cada {Horas} h · retención {Dias} días · {Vacuum}",
            Config.LimpiezaCadaHoras, Config.RetencionDias, vacuum);

        // Bucle principal. La primera pasada es INMEDIATA: al arrancar el
        // servidor ya se limpia lo acumulado mientras estuvo apagado, y se ve
        // en el log (que es lo que permite comprobar el comportamiento en
        // clase). Después, una vez por intervalo.
        //
        // El orden es "limpiar, esperar, limpiar, esperar...": así el intervalo
        // mide el tiempo ENTRE limpiezas y no se acumula retraso si una tarda.
        //
        // Fíjate que NO hay try/catch de fallos aquí: es LimpiarAsync quien
        // registra sus propios errores y nunca lanza (salvo cancelación). El
        // bucle solo tiene que preocuparse del apagado.
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await LimpiarAsync(stoppingToken);
                await Task.Delay(intervalo, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado normal: el host cancela el token y corta la espera (o
            // una operación a medias). El bucle se sale sin marcar error.
        }

        logger.LogInformation("🧹 Limpieza programada detenida");
    }

    /// <summary>
    /// Ejecuta UNA pasada de mantenimiento: borra los mensajes que superan la
    /// retención y, si la base de datos queda muy fragmentada, la compacta con
    /// un <c>VACUUM</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <c>public</c> para poder probar una pasada completa SIN tener que
    /// esperar al intervalo real: los tests la invocan directamente. El
    /// bucle de <c>ExecuteAsync</c> no hace más que llamarla cada N horas.
    /// </para>
    /// <para>
    /// <b>Nunca lanza excepciones de mantenimiento</b> (salvo cancelación): un
    /// disco lleno o una base bloqueada se registra en el log y se queda aquí.
    /// Es una decisión deliberada: un error de limpieza no debe tumbar ni al
    /// servidor ni al propio bucle, que reintentará en el siguiente intervalo.
    /// </para>
    /// </remarks>
    /// <param name="cancelacion">Token para cancelar la operación (apagado).</param>
    public async Task LimpiarAsync(CancellationToken cancelacion = default)
    {
        try
        {
            await LimpiarInternaAsync(cancelacion);
        }
        catch (OperationCanceledException)
        {
            // El apagado NO es un error: se propaga para que ExecuteAsync salga
            // de su bucle limpiamente. Si se tragara aquí, el servicio
            // seguiría "limpiando" contra una base que ya se está cerrando.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "⚠️ Falló la limpieza programada; se reintentará en la próxima pasada");
        }
    }

    /// <summary>
    /// Parte real de una pasada de mantenimiento: primero el borrado, luego la
    /// compactación. Está separada de <see cref="LimpiarAsync"/> solo para que
    /// el manejo de excepciones quede en un único sitio y legible.
    /// </summary>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    private async Task LimpiarInternaAsync(CancellationToken cancelacion)
    {
        var config = Config;

        // ── 1) DELETE: quitar lo que supera la retención ──────────────────
        // Con 0 días la orden es "conservar TODO", así que no se pide nada de
        // borrado. El repositorio ya deja en el log cuántos mensajes quitó.
        if (config.RetencionDias > 0)
        {
            var corte = DateTime.UtcNow.AddDays(-config.RetencionDias);
            await repository.LimpiarAsync(corte, cancelacion);
        }

        // ── 2) VACUUM: compactar SOLO si hay mucho hueco ──────────────────
        // Con 0 el usuario ha dicho "nunca compactes", y así se evita el
        // coste de reescribir el fichero entero sin motivo.
        if (config.VacuumSiFragmentado <= 0)
            return;

        // Se mide la fragmentación REAL con los PRAGMA de SQLite (páginas
        // libres / páginas totales). NO se usa el tamaño del fichero porque
        // el proyecto corre en modo WAL: ahí los datos recién escritos viven
        // en el fichero -.db-wal, así que el .db se queda en una página de
        // cabecera y su tamaño no refleja nada.
        var fragmentacion = await repository.GetFragmentacionAsync(cancelacion);

        if (fragmentacion < config.VacuumSiFragmentado)
        {
            logger.LogDebug(
                "🧹 Fragmentación {Fragmentacion:0.##}% por debajo del umbral ({Umbral}%): sin VACUUM",
                fragmentacion, config.VacuumSiFragmentado);
            return;
        }

        // El VACUUM puede fallar con "database is locked" si hay lecturas
        // concurrentes (alguien pidiendo el historial). No es grave: lo traga
        // LimpiarAsync, que lo deja anotado en el log, y se reintentará en la
        // próxima pasada.
        await repository.CompactarAsync(cancelacion);

        logger.LogInformation(
            "🧹 VACUUM ejecutado: la base estaba al {Fragmentacion:0.##}% fragmentada (umbral {Umbral}%)",
            fragmentacion, config.VacuumSiFragmentado);
    }
}
