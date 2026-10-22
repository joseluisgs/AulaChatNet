using AulaChatNet.Shared.Constantes;
using AulaChatNet.Shared.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace AulaChatNet.Client.Terminal.Tests.Servicios;

/// <summary>
/// Servidor mínimo de mentira para los tests: Kestrel real escuchando en un
/// puerto aleatorio de 127.0.0.1. Hay DOS versiones:
/// <list type="bullet">
/// <item>
/// <description><see cref="CrearAsync"/>: un hub que SIEMPRE rechaza el alta
/// (como el servidor real cuando el nick ya está en uso).
/// </description>
/// </item>
/// <item>
/// <description><see cref="CrearAceptandoAsync"/>: un hub que ACEPTA y deja que
/// el test DIFUNDA los eventos del servidor real (Fase 4.4: MensajeEditado,
/// MensajeBorrado...), para probar los manejadores de ClienteChat de verdad.
/// </description>
/// </item>
/// </list>
///
/// <remarks>
/// No se puede usar el TestServer de <c>Microsoft.AspNetCore.Mvc.Testing</c>:
/// su transporte es en memoria y un cliente SignalR real necesita un HTTP de
/// verdad. Kestrel en un puerto aleatorio sí, y el puerto lo asigna el
/// sistema — así varios tests pueden convivir sin pisarse.
/// </remarks>
/// </summary>
public sealed class ServidorDePrueba : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly EstadoPrueba _estado;
    private readonly IHubContext<HubDePruebas>? _contexto;

    private ServidorDePrueba(WebApplication app, EstadoPrueba estado, IHubContext<HubDePruebas>? contexto)
    {
        _app = app;
        _estado = estado;
        _contexto = contexto;
    }

    /// <summary>URL base donde está escuchando (p. ej. http://127.0.0.1:41234).</summary>
    public string Url => _app.Urls.First();

    /// <summary>Lo que el hub de pruebas devolverá al editar (null = éxito).</summary>
    public string? RespuestaEdicion
    {
        get => _estado.RespuestaEdicion;
        set => _estado.RespuestaEdicion = value;
    }

    /// <summary>Lo que el hub de pruebas devolverá al borrar (null = éxito).</summary>
    public string? RespuestaBorrado
    {
        get => _estado.RespuestaBorrado;
        set => _estado.RespuestaBorrado = value;
    }

    /// <summary>
    /// Crea el servidor que RECHAZA el alta: factory asíncrono en vez de
    /// constructor, porque arrancar Kestrel no puede hacerse en un ctor.
    /// </summary>
    /// <returns>El servidor ya escuchando.</returns>
    public static Task<ServidorDePrueba> CrearAsync() =>
        CrearAsync<HubQueRechaza>(new EstadoPrueba(), conContexto: false);

    /// <summary>
    /// Crea el servidor que ACEPTA el alta y deja DIFUNDIR eventos como el
    /// hub real (Fase 4.4): el test actúa a la vez que cliente y servidor.
    /// </summary>
    /// <returns>El servidor ya escuchando, con métodos de difusión activos.</returns>
    public static Task<ServidorDePrueba> CrearAceptandoAsync() =>
        CrearAsync<HubDePruebas>(new EstadoPrueba(), conContexto: true);

    private static async Task<ServidorDePrueba> CrearAsync<THub>(EstadoPrueba estado, bool conContexto)
        where THub : Hub
    {
        var builder = WebApplication.CreateBuilder();

        // Puerto 0 = que el sistema elija uno libre
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Los hubs no funcionan sin estos servicios (MapHub los exige)
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(estado);

        var app = builder.Build();
        app.MapHub<THub>("/hubs/chat"); // misma ruta que el servidor real

        await app.StartAsync();

        // El contexto solo existe si el hub mapeado es HubDePruebas: con el
        // hub que rechaza no hay nada que difundir
        var contexto = conContexto
            ? app.Services.GetRequiredService<IHubContext<HubDePruebas>>()
            : null;

        return new ServidorDePrueba(app, estado, contexto);
    }

    /// <summary>Difunde un MensajeNuevo al canal de la última conexión del cliente.</summary>
    /// <param name="mensaje">Mensaje tal como lo mandaría el servidor real.</param>
    public Task DifundirMensajeNuevoAsync(MensajeDto mensaje) =>
        DifundirAsync(ChatHubEventos.MensajeNuevo, mensaje);

    /// <summary>Difunde un MensajeEditado (mensaje COMPLETO) al canal conectado.</summary>
    /// <param name="editado">El mensaje con el texto nuevo y Editado = true.</param>
    public Task DifundirMensajeEditadoAsync(MensajeDto editado) =>
        DifundirAsync(ChatHubEventos.MensajeEditado, editado);

    /// <summary>Difunde un MensajeBorrado (solo el Id) al canal conectado.</summary>
    /// <param name="idMensaje">Id del mensaje que el servidor borró.</param>
    public Task DifundirMensajeBorradoAsync(Guid idMensaje) =>
        DifundirAsync(ChatHubEventos.MensajeBorrado, idMensaje);

    /// <summary>Difunde el estado COMPLETO de los chips de UN mensaje.</summary>
    /// <param name="reacciones">Chips del mensaje (lista vacía = se quitan todos).</param>
    public Task DifundirReaccionActualizadaAsync(ReaccionesMensajeDto reacciones) =>
        DifundirAsync(ChatHubEventos.ReaccionActualizada, reacciones);

    private Task DifundirAsync(string evento, object payload) =>
        _contexto!.Clients.Group(_estado.UltimoCanal!).SendAsync(evento, payload);

    /// <summary>Cierra el servidor (los tests lo hacen con <c>await using</c>).</summary>
    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>Lo que el hub de prueba recuerda entre invocaciones.</summary>
    public sealed class EstadoPrueba
    {
        /// <summary>Canal del último alta: para difundir al mismo grupo.</summary>
        public string? UltimoCanal { get; set; }

        /// <summary>Respuesta a EditarMensajeAsync (null = éxito).</summary>
        public string? RespuestaEdicion { get; set; }

        /// <summary>Respuesta a BorrarMensajeAsync (null = éxito).</summary>
        public string? RespuestaBorrado { get; set; }
    }

    /// <summary>
    /// El hub de mentira que SIEMPRE RECHAZA: reproduce el único caso que
    /// nos interesa probar desde el cliente — el servidor responde con un rechazo.
    /// </summary>
    public sealed class HubQueRechaza : Hub
    {
        /// <summary>Mismo nombre y firma que el hub real: siempre rechaza.</summary>
        /// <param name="usuario">Nick que intenta entrar.</param>
        /// <param name="canal">Canal al que intenta entrar.</param>
        /// <returns>El mensaje de rechazo, como lo devolvería el servidor real.</returns>
        public Task<string?> ConnectAsync(string usuario, string canal) =>
            Task.FromResult<string?>($"Ya hay un {usuario} en {canal}");
    }

    /// <summary>
    /// El hub de mentira que ACEPTA (Fase 4.4): da de alta en el grupo del
    /// canal — así los eventos difundidos por el test llegan al cliente, igual
    /// que los del hub real— y responde a Editar/Borrar según lo que el test
    /// haya preparado, para probar también los métodos salientes.
    /// </summary>
    /// <param name="estado">Memoria compartida con el servidor de prueba.</param>
    public sealed class HubDePruebas(EstadoPrueba estado) : Hub
    {
        /// <summary>Alta aceptada: apunta el canal y entra en su grupo.</summary>
        /// <param name="usuario">Nick del cliente (aquí se acepta siempre).</param>
        /// <param name="canal">Canal al que se da de alta.</param>
        /// <returns>null, que en el contrato real significa "todo correcto".</returns>
        [HubMethodName(ChatHubMetodos.Conectar)]
        public async Task<string?> ConnectAsync(string usuario, string canal)
        {
            estado.UltimoCanal = canal;
            await Groups.AddToGroupAsync(Context.ConnectionId, canal);

            return null;
        }

        /// <summary>Mismo nombre que el hub real: devuelve lo que el test haya preparado.</summary>
        /// <param name="idMensaje">Mensaje que el cliente quiere editar.</param>
        /// <param name="texto">Texto nuevo.</param>
        /// <returns>null si el test no preparó rechazo; su mensaje si lo hizo.</returns>
        [HubMethodName(ChatHubMetodos.Editar)]
        public Task<string?> EditarMensajeAsync(Guid idMensaje, string texto) =>
            Task.FromResult(estado.RespuestaEdicion);

        /// <summary>Mismo nombre que el hub real: devuelve lo que el test haya preparado.</summary>
        /// <param name="idMensaje">Mensaje que el cliente quiere borrar.</param>
        /// <returns>null si el test no preparó rechazo; su mensaje si lo hizo.</returns>
        [HubMethodName(ChatHubMetodos.Borrar)]
        public Task<string?> BorrarMensajeAsync(Guid idMensaje) =>
            Task.FromResult(estado.RespuestaBorrado);
    }
}
