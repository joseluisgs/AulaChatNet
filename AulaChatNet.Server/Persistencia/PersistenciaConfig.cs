namespace AulaChatNet.Server.Persistencia;

/// <summary>
/// Opciones tipadas de la persistencia (Fase 2), leídas de la sección
/// "Persistencia" de appsettings.json. Vive en el servidor y SOLO en el
/// servidor: los clientes no tienen por qué saber que hay una base de datos,
/// solo reciben los mensajes por SignalR como siempre.
///
/// <remarks>
/// Igual que <c>ChatConfig</c>, los valores por defecto son la última red de
/// seguridad: si falta la sección en appsettings, la app arranca con la
/// configuración razonable en vez de fallar.
/// </remarks>
/// </summary>
/// <example>
/// appsettings.json:
/// <code>
/// {
///   "Persistencia": {
///     "Ruta": "Data/aulachat.db",
///     "MigrarAutomaticamente": true,
///     "LimpiarAlArrancar": false,
///     "PreloadMensajes": 50,
///     "LimpiezaCadaHoras": 24,
///     "RetencionDias": 0,
///     "VacuumSiFragmentado": 25
///   }
/// }
/// </code>
/// </example>
public class PersistenciaConfig
{
    /// <summary>Nombre de la sección de configuración en appsettings.json.</summary>
    public const string Seccion = "Persistencia";

    /// <summary>
    /// Carpeta donde vive la base de datos SQLite (un solo fichero).
    /// Ruta RELATIVA a la carpeta de contenido del servidor.
    /// </summary>
    /// <remarks>
    /// SQLite crea el fichero pero NO la carpeta que lo contiene: si "Data"
    /// no existe, la primera ejecución revienta con "unable to open database
    /// file". Por eso el arrancador la crea con Directory.CreateDirectory.
    /// </remarks>
    /// <example>Data/aulachat.db</example>
    public string Ruta { get; set; } = "Data/aulachat.db";

    /// <summary>
    /// Si es true, al arrancar se aplican las migraciones pendientes
    /// (<c>Database.Migrate()</c>): crea o actualiza el esquema sin tocar los
    /// datos existentes.
    /// </summary>
    /// <remarks>
    /// Se usa Migrate() y no EnsureCreated() a propósito: EnsureCreated crea
    /// el esquema de cero y lo RECREA si detecta cambios, lo que destruiría
    /// los datos guardados. Migrate() aplica solo lo que falta y conserva todo.
    /// </remarks>
    public bool MigrarAutomaticamente { get; set; } = true;

    /// <summary>
    /// Si es true, al arrancar se BORRA la base de datos completa y se recrea
    /// desde cero: el historial arranca vacío en cada ejecución.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es una llave de desarrollo para cuando quieres empezar de cero sin
    /// borrar a mano el fichero .db. En producción NUNCA se activa.
    /// </para>
    /// <para>
    /// Está como CLAVE DE CONFIGURACIÓN y no amarrada al entorno
    /// (Development/Production) a propósito. La Fase 2 existe justamente para
    /// que el historial SOBREVIVA a un reinicio; si el arranque en desarrollo
    /// limpiase siempre la base de datos, nunca podrías comprobar ese
    /// comportamiento en el entorno donde desarrollas y demuestras. Además, el
    /// borrado silencioso por entorno engaña: crees que tu código persiste y no
    /// es así. Con una clave explícita la decisión es tuya y visible.
    /// Para "en desarrollo empiezo limpio", ponla en true en
    /// appsettings.Development.json: obtienes el mismo efecto, pero como
    /// elección consciente.
    /// </para>
    /// </remarks>
    /// <example>false</example>
    public bool LimpiarAlArrancar { get; set; } = false;

    /// <summary>
    /// Nº máximo de mensajes que se cargan en la caché en memoria al arrancar
    /// (los que se reenvían al conectar al canal).
    /// </summary>
    /// <remarks>
    /// No confundir con <c>Chat:HistorialMaximo</c>, que es lo que se MANTIENE
    /// en la caché durante la sesión. Este valor solo decide cuántos se traen
    /// del disco al encender el servidor.
    /// </remarks>
    /// <example>50</example>
    public int PreloadMensajes { get; set; } = 50;

    /// <summary>
    /// Cada cuántas horas se ejecuta la limpieza de mensajes antiguos.
    /// 0 = la limpieza automática queda desactivada (útil en desarrollo).
    /// </summary>
    /// <example>24</example>
    public int LimpiezaCadaHoras { get; set; } = 24;

    /// <summary>
    /// Días que se conserva un mensaje antes de que la limpieza lo borre.
    /// 0 = conservarlo todo (sin límite de retención).
    /// </summary>
    /// <remarks>
    /// Aunque SQLite no devuelve el espacio al sistema operativo al borrar,
    /// SÍ lo reutiliza: las páginas libres se llenan con los mensajes nuevos.
    /// Así que el DELETE solo ya frena el crecimiento; el VACUUM solo es
    /// necesario para recoger espacio ya usado.
    /// </remarks>
    /// <example>90</example>
    public int RetencionDias { get; set; } = 0;

    /// <summary>
    /// Umbral de fragmentación (en % de páginas libres) a partir del cual la
    /// limpieza ejecuta un VACUUM para devolver el espacio al disco.
    /// 0 = nunca ejecutar VACUUM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El VACUUM reescribe la base de datos entera para compactarla, así que
    /// es CARO: no se hace en cada limpieza, solo cuando merece la pena. Se
    /// mide la fragmentación real con <c>PRAGMA freelist_count</c> y
    /// <c>PRAGMA page_count</c> (páginas libres sobre páginas totales), que es
    /// la señal honesta de "cuánto espacio se está desperdiciando".
    /// </para>
    /// <para>
    /// Un valor razonable está entre 20 y 30: por debajo, el espacio libre es
    /// poco y SQLite lo reutilizará con los mensajes nuevos sin necesidad de
    /// reescribir nada.
    /// </para>
    /// </remarks>
    /// <example>25</example>
    public int VacuumSiFragmentado { get; set; } = 25;

    /// <summary>
    /// Devuelve la cadena de conexión SQLite para la ruta configurada.
    /// </summary>
    /// <returns>Cadena de conexión para EF Core (proveedor SQLite).</returns>
    /// <remarks>
    /// "Cache=Shared" no va aquí: eso es un truco de testing para que los tests
    /// compartan memoria. En producción cada contexto abre y cierra su propia
    /// conexión, que es lo normal y lo correcto.
    /// </remarks>
    public string CadenaConexion() => $"Data Source={Ruta}";
}
