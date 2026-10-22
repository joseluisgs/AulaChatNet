using AulaChatNet.Shared.Dtos;

namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Contrato de persistencia del historial del chat (Fase 2). Separa lo DURADERO
/// de lo efímero: el <c>ChatStore</c> sigue guardando en memoria la presencia y
/// la unicidad de nicks (que mueren al reiniciar), y delega aquí SOLO los
/// mensajes, que son lo único que debe sobrevivir a un apagón.
///
/// <remarks>
/// <para>
/// Es asíncrono a diferencia de <c>IChatStore</c>, y esa diferencia es
/// deliberada: un hub de SignalR NUNCA debe bloquear esperando disco, porque
/// con pocos usuarios agotaría el pool de hilos. Por eso el <c>ChatStore</c>
/// (singleton) no llama directamente a EF Core, sino que encola y un servicio
/// en segundo plano hace la escritura.
/// </para>
/// <para>
/// El canal llega como texto canónico (p. ej. "#general"), resuelto ya por el
/// hub con <c>ChatConfig.EncontrarCanal</c>.
/// </para>
/// </remarks>
/// </summary>
public interface IMensajesRepository
{
    /// <summary>
    /// Guarda un mensaje en la base de datos.
    /// </summary>
    /// <param name="canal">Canal canónico donde se escribió.</param>
    /// <param name="mensaje">Mensaje a persistir.</param>
    /// <param name="cancelacion">Token para cancelar la operación (cierre del servidor).</param>
    Task GuardarAsync(string canal, MensajeDto mensaje, CancellationToken cancelacion = default);

    /// <summary>
    /// Devuelve los últimos mensajes de un canal, del más antiguo al más reciente
    /// (mismo orden que devuelve <c>IChatStore.GetHistorial</c>).
    /// </summary>
    /// <param name="canal">Canal canónico.</param>
    /// <param name="limite">Número máximo de mensajes a devolver.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>Lista ordenada cronológicamente, vacía si el canal no tiene mensajes.</returns>
    Task<IReadOnlyList<MensajeDto>> GetUltimosAsync(string canal, int limite, CancellationToken cancelacion = default);

    /// <summary>
    /// Devuelve una PÁGINA de mensajes del canal hacia ATRÁS en el tiempo: los
    /// anteriores (o del mismo instante) a la marca de tiempo indicada, del más
    /// antiguo al más reciente. Es el cimiento del "cargar más mensajes" de la
    /// Fase 4.3.
    /// </summary>
    /// <remarks>
    /// El cursor <paramref name="antesDe"/> es INCLUSIVO: además de lo
    /// anterior devuelve también los del MISMO instante, que con una sola
    /// fecha no se pueden separar. Quien recibe la página descarta por Id los
    /// mensajes que ya tiene, y así no se pierde ninguno.
    /// </remarks>
    /// <param name="canal">Canal canónico.</param>
    /// <param name="antesDe">Marca de tiempo (UTC) del mensaje más antiguo que ya ve el cliente.</param>
    /// <param name="limite">Tamaño máximo de la página.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>La página en orden cronológico; vacío = no queda más historial.</returns>
    Task<IReadOnlyList<MensajeDto>> GetAnterioresAsync(
        string canal, DateTime antesDe, int limite, CancellationToken cancelacion = default);

    /// <summary>
    /// Borra los mensajes anteriores a una fecha dada y devuelve cuántos ha quitado.
    /// Es la base del mantenimiento del fichero: sin él, la base de datos crece
    /// sin límite.
    /// </summary>
    /// <param name="anteriorA">Momento (UTC) a partir del cual se conserva todo.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>Número de mensajes eliminados.</returns>
    Task<int> LimpiarAsync(DateTime anteriorA, CancellationToken cancelacion = default);

    /// <summary>
    /// Actualiza el texto de un mensaje existente y lo marca como editado
    /// (Fase 4.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A diferencia de <see cref="GuardarAsync"/>, esta ESCRITURA va DIRECTA
    /// a la base de datos y no pasa por la cola en segundo plano: la cola es
    /// de insertados (write-behind) y una fila que aún no existe no se puede
    /// actualizar dentro de ella. Mismo criterio que <see cref="LimpiarAsync"/>,
    /// que también sale fuera de la cola.
    /// </para>
    /// <para>
    /// Carrera conocida y documentada: si alguien edita un mensaje en los
    /// pocos milisegundos en los que su INSERT aún viaja por la cola, el
    /// UPDATE no encuentra la fila y devuelve false. En el aula ese margen es
    /// inalcanzable tecleando, y el store deja constancia en el log.
    /// </para>
    /// </remarks>
    /// <param name="mensaje">Mensaje con el texto nuevo (y Editado = true); su Id es la clave del UPDATE.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>true si se ha actualizado una fila; false si esa fila no existe.</returns>
    Task<bool> ActualizarAsync(MensajeDto mensaje, CancellationToken cancelacion = default);

    /// <summary>
    /// Borra un mensaje de la base de datos por su Id (Fase 4.4). Es el
    /// borrado definitivo: no queda copia en disco ni en la paginación.
    /// </summary>
    /// <param name="idMensaje">Identificador del mensaje a eliminar.</param>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>true si se ha borrado una fila; false si esa fila no existe.</returns>
    Task<bool> BorrarAsync(Guid idMensaje, CancellationToken cancelacion = default);

    // ─────────────────────────────────────────────────────────────────────
    // Mantenimiento de SQLite. Estos dos métodos no hablan de "mensajes" sino
    // de la BASE DE DATOS, y viven aquí por una decisión de diseño: el
    // repositorio es la puerta ÚNICA a la base de datos, y preferimos eso a
    // repartir el SQL por varios sitios. Así, todo el SQL del servidor está en
    // un solo fichero (MensajeRepository.cs) y el servicio de limpieza queda
    // como un orquestador puro, sin una línea de SQL, que se puede testear
    // con un simple mock.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve la fragmentación actual de la base de datos, como porcentaje
    /// de páginas libres sobre el total (0 = nada desperdiciado, 100 = todo
    /// hueco).
    /// </summary>
    /// <remarks>
    /// Es la señal HONESTA de "cuánto espacio se tira", y se mide con los
    /// PRAGMA <c>freelist_count</c> y <c>page_count</c>. Se usa esta y NO el
    /// tamaño del fichero porque el proyecto corre en modo WAL: ahí los datos
    /// recién escritos viven en el fichero <c>-.db-wal</c>, así que el
    /// <c>.db</c> se queda en una sola página de cabecera y su tamaño no
    /// refleja nada.
    /// </remarks>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    /// <returns>Porcentaje de páginas libres, entre 0 y 100.</returns>
    Task<double> GetFragmentacionAsync(CancellationToken cancelacion = default);

    /// <summary>
    /// Ejecuta un <c>VACUUM</c>: reescribe la base de datos entera por una
    /// versión compacta y devuelve las páginas libres al fichero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la ÚNICA forma de que SQLite devuelva de verdad el espacio: el
    /// <c>DELETE</c> solo marca páginas como libres para reutilizarlas, no las
    /// suelta. Por eso es una operación cara (reescribe todo) y no se hace en
    /// cada limpieza, solo cuando la fragmentación supera el umbral.
    /// </para>
    /// <para>
    /// En modo WAL puede fallar con "database is locked" si hay lecturas
    /// concurrentes (p. ej. alguien pidiendo el historial). El servicio de
    /// limpieza lo traga y lo deja anotado en el log: reintentará en la
    /// siguiente pasada.
    /// </para>
    /// </remarks>
    /// <param name="cancelacion">Token para cancelar la operación.</param>
    Task CompactarAsync(CancellationToken cancelacion = default);
}
