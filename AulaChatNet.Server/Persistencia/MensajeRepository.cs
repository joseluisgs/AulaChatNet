using AulaChatNet.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Implementación de <see cref="IMensajesRepository"/> sobre SQLite con EF Core.
/// El historial deja de morir con el proceso: sobrevive a cualquier reinicio.
///
/// <remarks>
/// <para>
/// Recibe un <c>IDbContextFactory</c> y NO un <c>DbContext</c> directo. La razón
/// es de arquitectura: el <c>ChatStore</c> que lo usa es un <c>singleton</c>, y
/// un <c>DbContext</c> es de corta vida (scoped) y NO es thread-safe. La fábrica
/// permite crear un contexto nuevo y desechable por cada operación, que es el
/// patrón recomendado cuando un singleton necesita acceso a EF Core.
/// </para>
/// <para>
/// <b>Solo se persisten los mensajes NORMALES.</b> Los de sistema («Ana ha
/// entrado», «Ana ahora se llama…») describen hechos que ya pasaron y solo
/// tienen sentido en la sesión viva: guardarlos sería guardar logs, no
/// conversación. Por eso la caché en memoria del ChatStore los conserva
/// mientras dura la sesión, pero la base de datos no.
/// </para>
/// </remarks>
/// </summary>
public class MensajeRepository(IDbContextFactory<AulaChatContexto> fabrica, ILogger<MensajeRepository> logger)
    : IMensajesRepository
{
    /// <inheritdoc />
    public async Task GuardarAsync(string canal, MensajeDto mensaje, CancellationToken cancelacion = default)
    {
        // Política de persistencia: el ruido de sistema no se guarda.
        if (mensaje.Tipo != TipoMensaje.Normal)
            return;

        // Contexto nuevo y desechable por operación (la fábrica lo cede)
        await using var contexto = await fabrica.CreateDbContextAsync(cancelacion);

        contexto.Mensajes.Add(new MensajeEntidad
        {
            Id = mensaje.Id,                          // el GUID que ya trae el DTO
            Canal = canal,
            Usuario = mensaje.Usuario,
            Texto = mensaje.Texto,
            Timestamp = mensaje.Timestamp,            // siempre UTC, contrato de la entidad
            Tipo = mensaje.Tipo
        });

        await contexto.SaveChangesAsync(cancelacion);

        logger.LogDebug("Mensaje {Id} persistido en {Canal}", mensaje.Id, canal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MensajeDto>> GetUltimosAsync(
        string canal, int limite, CancellationToken cancelacion = default)
    {
        await using var contexto = await fabrica.CreateDbContextAsync(cancelacion);

        // El índice (Canal, Timestamp) hace que esto sea un barrido por un
        // rango ordenado, no un ordenamiento de la tabla entera. Se pide a la
        // base de datos SOLO los últimos N (Take) y en el orden inverso al que
        // se necesitan, que es más barato que paginar de atrás hacia delante.
        var entidades = await contexto.Mensajes
            .AsNoTracking()                            // solo lectura: sin tracking
            .Where(m => m.Canal == canal)
            .OrderByDescending(m => m.Timestamp)
            .Take(limite)
            .ToListAsync(cancelacion);

        // Se devuelven del más antiguo al más reciente, igual que GetHistorial
        entidades.Reverse();

        // SQLite devuelve el Kind perdido (Unspecified) porque guarda fechas
        // como texto. El contrato de la entidad es "siempre UTC", así que se
        // devuelve el valor a UTC explícitamente. Sin este paso, los clientes
        // convertirían mal la hora y verían la del servidor.
        return entidades
            .Select(m => new MensajeDto(
                m.Id, m.Usuario, m.Texto,
                DateTime.SpecifyKind(m.Timestamp, DateTimeKind.Utc),
                m.Tipo))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<int> LimpiarAsync(DateTime anteriorA, CancellationToken cancelacion = default)
    {
        await using var contexto = await fabrica.CreateDbContextAsync(cancelacion);

        // ExecuteDeleteAsync genera UNA instrucción DELETE en SQL, sin traer
        // las filas a memoria. Es mucho mejor que cargarlas y borrar una a una.
        var borrados = await contexto.Mensajes
            .Where(m => m.Timestamp < anteriorA)
            .ExecuteDeleteAsync(cancelacion);

        if (borrados > 0)
            logger.LogInformation(
                "Limpieza: {Borrados} mensajes anteriores a {Fecha} eliminados", borrados, anteriorA);

        return borrados;
    }

    /// <inheritdoc />
    public async Task<double> GetFragmentacionAsync(CancellationToken cancelacion = default)
    {
        await using var contexto = await fabrica.CreateDbContextAsync(cancelacion);

        // Los PRAGMA no son un SELECT de tabla, así que no valen las consultas
        // LINQ: se leen con SqlQueryRaw. Hay DOS trampas de EF aquí, y las dos
        // solo salen al ejecutar:
        //  1) EF mete la instrucción dentro de un FROM, así que "PRAGMA
        //     page_count" a secas NO sirve: no es un SELECT componible y EF lo
        //     rechaza con CheckComposableSql. La forma válida en SQLite moderno
        //     es la función de tabla del PRAGMA, que sí es un SELECT.
        //  2) Para un tipo escalar EF envuelve el resultado como
        //     SELECT [s].[Value] FROM (...) AS [s], así que la columna debe
        //     llamarse exactamente Value; si no, SQLite falla con
        //     "no such column: s.Value".
        var paginas = await contexto.Database
            .SqlQueryRaw<long>("SELECT page_count AS Value FROM pragma_page_count()")
            .SingleAsync(cancelacion);

        if (paginas <= 0)
            return 0;                       // base vacía o recién creada: nada que medir

        var libres = await contexto.Database
            .SqlQueryRaw<long>("SELECT freelist_count AS Value FROM pragma_freelist_count()")
            .SingleAsync(cancelacion);

        return libres * 100.0 / paginas;
    }

    /// <inheritdoc />
    public async Task CompactarAsync(CancellationToken cancelacion = default)
    {
        await using var contexto = await fabrica.CreateDbContextAsync(cancelacion);

        // VACUUM no puede ejecutarse DENTRO de una transacción, y
        // ExecuteSqlRawAsync no abre ninguna por su cuenta, así que aquí es
        // seguro. Reescribe la base de datos entera: es caro, por eso solo se
        // invoca desde la limpieza cuando la fragmentación merece la pena.
        await contexto.Database.ExecuteSqlRawAsync("VACUUM", cancelacion);

        logger.LogDebug("VACUUM ejecutado: base de datos compactada");
    }
}
