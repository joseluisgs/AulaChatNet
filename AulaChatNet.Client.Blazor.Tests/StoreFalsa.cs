using System.Reactive.Subjects;
using AulaChatNet.Client.Blazor.State;
using AulaChatNet.Shared.Dtos;
using Moq;

namespace AulaChatNet.Client.Blazor.Tests;

/// <summary>
/// Store falsa para los tests de componentes: en lugar de una conexión a
/// SignalR real, expone <c>BehaviorSubject</c> que el test "menea" para
/// simular lo que llegaría del servidor, y deja verificar las llamadas
/// (Moq) sin red.
///
/// <remarks>
/// Es el mismo enfoque doble que el del proyecto: los stores se prueban con
/// red de verdad (o sin ella), y los COMPONENTES contra esta doble, porque
/// a un componente no le importa de dónde vienen los datos, sino cómo los
/// pinta y qué le dice al pulsar.
///
/// Los "valores actuales" (Canal, EstadoValor, VistaUsuarios,
/// EstadisticasActuales) se resuelven en el MOMENTO de la llamada contra el
/// sujeto correspondiente: "menear" el flujo desde el test cambia lo que
/// vean el procesador de comandos y los componentes.
/// </remarks>
/// </summary>
public class StoreFalsa
{
    private readonly Mock<IChatStore> _mock = new();

    /// <summary>Flujo de mensajes para inyectar lo que "llega del servidor".</summary>
    public BehaviorSubject<IReadOnlyList<MensajeDto>> Mensajes { get; } = new([]);

    /// <summary>Flujo de privados: solo SE AÑADEN (el servidor no guarda ninguno).</summary>
    public BehaviorSubject<IReadOnlyList<MensajePrivadoDto>> Privados { get; } = new([]);

    /// <summary>Flujo de conectados del canal actual.</summary>
    public BehaviorSubject<IReadOnlyList<UsuarioConectadoDto>> Usuarios { get; } = new([]);

    /// <summary>Flujo de estadísticas globales (total y por sala).</summary>
    public BehaviorSubject<EstadisticasDto> Estadisticas { get; } = new(new(0, []));

    /// <summary>Flujo del canal canónico en el que estamos.</summary>
    public BehaviorSubject<string?> CanalActual { get; } = new(null);

    /// <summary>Flujo del nick actual.</summary>
    public BehaviorSubject<string?> Nick { get; } = new(null);

    /// <summary>Flujo del estado de la conexión.</summary>
    public BehaviorSubject<EstadoConexion> Estado { get; } = new(EstadoConexion.Conectado);

    /// <summary>Flujo de sesión activa (lo que decide qué mitad pinta la página).</summary>
    public BehaviorSubject<bool> EnSesion { get; } = new(false);

    /// <summary>Flujo de la persona elegida para escribirle un privado.</summary>
    public BehaviorSubject<string?> DestinatarioPrivado { get; } = new(null);

    /// <summary>Flujo de quienes están escribiendo en el canal.</summary>
    public BehaviorSubject<IReadOnlyList<string>> Escribiendo { get; } = new([]);

    /// <summary>Flujo de reacciones por mensaje del canal (chip por emoji).</summary>
    public BehaviorSubject<IReadOnlyDictionary<Guid, ReaccionesMensajeDto>> Reacciones { get; } =
        new(new Dictionary<Guid, ReaccionesMensajeDto>());

    /// <summary>Flujo de la última mención dirigida recibida.</summary>
    public BehaviorSubject<MencionDto?> UltimaMencion { get; } = new(null);

    /// <summary>Flujo del contador de menciones sin mirar (el badge).</summary>
    public BehaviorSubject<int> MencionesNoVistas { get; } = new(0);

    /// <summary>El objeto que se registra en los servicios del componente.</summary>
    public IChatStore Objeto => _mock.Object;

    /// <summary>El mock, para verificar llamadas (Verify).</summary>
    public Mock<IChatStore> Mock => _mock;

    private StoreFalsa(IReadOnlyList<string> canales)
    {
        _mock.Setup(s => s.Mensajes).Returns(Mensajes);
        _mock.Setup(s => s.Privados).Returns(Privados);
        _mock.Setup(s => s.Usuarios).Returns(Usuarios);
        _mock.Setup(s => s.Estadisticas).Returns(Estadisticas);
        _mock.Setup(s => s.CanalActual).Returns(CanalActual);
        _mock.Setup(s => s.Nick).Returns(Nick);
        _mock.Setup(s => s.Estado).Returns(Estado);
        _mock.Setup(s => s.EnSesion).Returns(EnSesion);
        _mock.Setup(s => s.DestinatarioPrivado).Returns(DestinatarioPrivado);
        _mock.Setup(s => s.Escribiendo).Returns(Escribiendo);
        _mock.Setup(s => s.Reacciones).Returns(Reacciones);
        _mock.Setup(s => s.UltimaMencion).Returns(UltimaMencion);
        _mock.Setup(s => s.MencionesNoVistas).Returns(MencionesNoVistas);
        _mock.Setup(s => s.Canales).Returns(canales);

        // Valores actuales: leídos del SUJETO en cada llamada (lazy), así
        // que "menear" el flujo desde el test se refleja al instante
        _mock.Setup(s => s.Canal).Returns(() => CanalActual.Value);
        _mock.Setup(s => s.EstadoValor).Returns(() => Estado.Value);
        _mock.Setup(s => s.VistaUsuarios).Returns(() => Usuarios.Value);
        _mock.Setup(s => s.VistaPrivados).Returns(() => Privados.Value);
        _mock.Setup(s => s.VistaMensajes).Returns(() => Mensajes.Value);
        _mock.Setup(s => s.VistaReacciones).Returns(() => Reacciones.Value);
        _mock.Setup(s => s.EstadisticasActuales).Returns(() => Estadisticas.Value);
    }

    /// <summary>
    /// Crea la store falsa con los canales dados (por defecto, los dos de
    /// pruebas: #general y #daw).
    /// </summary>
    /// <param name="canales">Canales a ofrecer en las píldoras.</param>
    public static StoreFalsa Crear(IReadOnlyList<string>? canales = null) =>
        new(canales ?? ["#general", "#daw"]);
}
