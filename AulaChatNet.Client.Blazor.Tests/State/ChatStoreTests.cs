using AulaChatNet.Client.Blazor.State;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using FluentAssertions;

namespace AulaChatNet.Client.Blazor.Tests.State;

/// <summary>
/// Pruebas de la store de chat con el SERVIDOR CAÍDO: es el caso que más
/// duele en un aula, y el contrato que hay que blindar (los errores se
/// traducen a palabras de usuario; jamás sale el tipo de una excepción).
///
/// <remarks>
/// El servidor de prueba REAL (Kestrel en puerto aleatorio) vive en los tests
/// de la terminal; aquí lo que se prueba es el lado del cliente: qué devuelve
/// la store cuando no hay nadie al otro lado.
/// </remarks>
/// </summary>
public class ChatStoreTests
{
    /// <summary>Puerto descartado: nadie escucha ahí, la conexión se rechaza al instante.</summary>
    private const string UrlMuerta = "http://127.0.0.1:59999/hubs/chat";

    private static ChatConfig Config() => new()
    {
        Canales = ["#general", "#daw"]
    };

    [Test]
    public void EstadoInicial_EstaDesconectadoYSinSesion()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        var estado = Capturar<EstadoConexion>(store.Estado);
        var enSesion = Capturar<bool>(store.EnSesion);

        // Assert
        estado.Should().Be(EstadoConexion.Desconectado);
        enSesion.Should().BeFalse();
    }

    [Test]
    public void Canales_VieneDeLaConfiguracion()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        var canales = store.Canales;

        // Assert
        canales.Should().Equal("#general", "#daw");
    }

    [Test]
    public async Task ConectarAsync_SinServidor_DevuelveFalloDeRedSinTiposDeExcepcion()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        var resultado = await store.ConectarAsync("Ana", "#general");

        // Assert: HayServidor=false → el caller NO debe repreguntar el nick,
        // solo ofrecer reintento. Y en el mensaje: palabras de usuario.
        resultado.HayServidor.Should().BeFalse();
        resultado.Exitoso.Should().BeFalse();
        resultado.Error.Should()
            .StartWith("No se pudo conectar con el servidor")
            .And.NotContain("Exception")
            .And.NotContain("HttpRequest");
    }

    [Test]
    public async Task ConectarAsync_SinServidor_NoDejaLaConexionEnganchada()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        await store.ConectarAsync("Ana", "#general");

        // Assert: vuelve a estar desconectado y sin sesión (nada de estados a medias)
        Capturar<EstadoConexion>(store.Estado).Should().Be(EstadoConexion.Desconectado);
        Capturar<bool>(store.EnSesion).Should().BeFalse();
    }

    [Test]
    public async Task EnviarAsync_SinConexion_DevuelveErrorDeUsuario()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        var error = await store.EnviarAsync("hola");

        // Assert: InvokeAsync LANZA con la conexión caída; la store lo
        // convierte al patrón string? con mensaje de usuario
        error.Should().Be("Sin conexión con el servidor");
    }

    [Test]
    public async Task CambiarCanal_SinConexion_DevuelveErrorDeUsuario()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        var error = await store.CambiarCanalAsync("#daw");

        // Assert
        error.Should().Be("Sin conexión con el servidor");
        Capturar<string?>(store.CanalActual).Should().BeNull("el canal no cambió sin conexión");
    }

    [Test]
    public async Task CambiarNick_SinConexion_DevuelveErrorDeUsuario()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        var error = await store.CambiarNickAsync("Anita");

        // Assert
        error.Should().Be("Sin conexión con el servidor");
    }

    [Test]
    public async Task ReconectarAsync_SinSesionPrevia_DevuelveError()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        var error = await store.ReconectarAsync();

        // Assert: sin identidad no hay nada que re-registrar (y se le DICE al usuario)
        error.Should().Be("No hay ninguna sesión previa para reconectar");
    }

    [Test]
    public async Task CerrarAsync_SinConexion_NoLanzaYNuncaDebeRomper()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // Act
        Func<Task> act = async () => await store.CerrarAsync();

        // Assert: una salida ordenada jamás lanza excepción (ni con la caída puesta)
        await act.Should().NotThrowAsync();
        Capturar<EstadoConexion>(store.Estado).Should().Be(EstadoConexion.Desconectado);
    }

    [Test]
    public void Aviso_EntraEnLaVistaComoMensajeDeSistema()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // El BehaviorSubject entrega el valor actual al suscribirse y sigue
        // vivo mientras la store exista: la suscripción acompaña al test
        IReadOnlyList<MensajeDto> vista = [];
        using var sub = store.Mensajes.Subscribe(mensajes => vista = mensajes);

        // Act
        store.Aviso("Se perdió la conexión con el servidor.");

        // Assert: el aviso del cliente se pinta EXACTAMENTE igual que uno
        // del servidor: por eso la UI no necesita distinguirlos
        vista.Should().HaveCount(1);
        vista[0].Tipo.Should().Be(TipoMensaje.Sistema);
        vista[0].Texto.Should().Be("Se perdió la conexión con el servidor.");
    }

    [Test]
    public void Aviso_AcumulaDetrasDeLoQueYaHabia()
    {
        // Arrange
        using var store = new ChatStore(UrlMuerta, Config());

        // La store REEMPLAZA la vista en cada cambio (como hace con los
        // componentes): la suscripción guarda el último valor
        IReadOnlyList<MensajeDto> vista = [];
        using var sub = store.Mensajes.Subscribe(mensajes => vista = mensajes);

        // Act
        store.Aviso("primer aviso");
        store.Aviso("segundo aviso");

        // Assert
        vista.Should().HaveCount(2);
        vista[1].Texto.Should().Be("segundo aviso");
    }

    // ── Ayudante ──

    /// <summary>Lee el valor ACTUAL de un observable (el BehaviorSubject lo guarda).</summary>
    private static T Capturar<T>(IObservable<T> fuente)
    {
        var valor = default(T)!;
        var entrego = false;

        using var sub = fuente.Subscribe(v =>
        {
            valor = v;
            entrego = true;
        });

        // El BehaviorSubject SIEMPRE entrega al suscribirse: si no lo hizo,
        // el test falla aquí y no se entiende "a medias" más adelante
        entrego.Should().BeTrue();

        return valor;
    }
}
