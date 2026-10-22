using AulaChatNet.Client.Web.Configuracion;
using AulaChatNet.Client.Web.Services.Storage;   // ILocalStorageService
using AulaChatNet.Client.Web.State;
using AulaChatNet.Client.Wpf.Infrastructures;
using AulaChatNet.Client.Wpf.Services;           // FileStorageService
using AulaChatNet.Shared.Configuracion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AulaChatNet.Client.Wpf.Tests.Infrastructures;

/// <summary>
/// Tests de la configuración y el registro de servicios del cliente de
/// escritorio: la URL del servidor (con su valor por defecto) y el mismo
/// esqueleto de DI que el host WebAssembly.
/// </summary>
[TestFixture]
public class ServicesConfigTests
{
    #region Configuración de prueba

    /// <summary>
    /// Construye una configuración REAL desde un JSON en disco temporal,
    /// igual que hace la app (AddJsonFile sobre appsettings.json).
    /// </summary>
    /// <param name="json">Contenido JSON de la configuración.</param>
    /// <returns>Configuración construida y lista para usar.</returns>
    private static IConfiguration CrearConfiguracion(string json)
    {
        // Arrange compartido: fichero temporal + AddJsonFile, el mismo camino
        // que recorre App.OnStartup con su appsettings.json
        var ruta = Path.Combine(Path.GetTempPath(), $"aulachat-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(ruta, json);

        try
        {
            return new ConfigurationBuilder().AddJsonFile(ruta).Build();
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    #endregion

    #region ResolverUrlServidor

    [Test]
    public void ResolverUrlServidor_ConVacia_DevuelveLaDelServidorLocal()
    {
        // Arrange
        var web = new WebConfig { UrlServidor = "" };

        // Act
        var resultado = ServicesConfig.ResolverUrlServidor(web);

        // Assert
        Assert.That(resultado, Is.EqualTo("http://localhost:5030"));
    }

    [Test]
    public void ResolverUrlServidor_ConEspacios_DevuelveLaDelServidorLocal()
    {
        // Arrange
        var web = new WebConfig { UrlServidor = "   " };

        // Act
        var resultado = ServicesConfig.ResolverUrlServidor(web);

        // Assert
        Assert.That(resultado, Is.EqualTo(ServicesConfig.UrlServidorPorDefecto));
    }

    [Test]
    public void ResolverUrlServidor_ConUrl_LaDevuelveSinBarraFinal()
    {
        // Arrange
        var web = new WebConfig { UrlServidor = "http://servidor-del-centro:5030/" };

        // Act
        var resultado = ServicesConfig.ResolverUrlServidor(web);

        // Assert: la barra final se recorta para poder concatenar /hubs/chat
        Assert.That(resultado, Is.EqualTo("http://servidor-del-centro:5030"));
    }

    #endregion

    #region AddClienteWpf

    [Test]
    public void AddClienteWpf_ConElAppsettingsReal_RegistraConfiguracionYStores()
    {
        // Arrange
        var config = CrearConfiguracion("""
            {
              "Chat": { "Canales": [ "#general", "#daw" ] },
              "Web": { "UrlServidor": "http://localhost:5030", "RecordarSesion": true }
            }
            """);
        var servicios = new ServiceCollection();

        // Act
        servicios.AddClienteWpf(config);
        using var proveedor = servicios.BuildServiceProvider();

        // Assert
        Assert.Multiple(() =>
        {
            // Las secciones quedan tipadas y registradas como singletons
            Assert.That(proveedor.GetRequiredService<ChatConfig>().Canales,
                Is.EqualTo(new[] { "#general", "#daw" }));
            Assert.That(proveedor.GetRequiredService<WebConfig>().UrlServidor,
                Is.EqualTo("http://localhost:5030"));

            // El HttpClient apunta a la raíz del servidor (para /estadisticas)
            Assert.That(proveedor.GetRequiredService<HttpClient>().BaseAddress,
                Is.EqualTo(new Uri("http://localhost:5030/")));

            // Y la store reactiva queda construida (misma que en la web)
            Assert.That(proveedor.GetService<IChatStore>(), Is.Not.Null);
        });
    }

    [Test]
    public void AddClienteWpf_SinSeccionWeb_UsaElServidorLocalPorDefecto()
    {
        // Arrange: configuración SIN la sección "Web" (fichero mínimo)
        var config = CrearConfiguracion("""{ "Chat": { "Canales": [ "#general" ] } }""");
        var servicios = new ServiceCollection();

        // Act
        servicios.AddClienteWpf(config);
        using var proveedor = servicios.BuildServiceProvider();

        // Assert: el cliente sigue siendo usable sin escribir URL alguna
        Assert.That(proveedor.GetRequiredService<HttpClient>().BaseAddress,
            Is.EqualTo(new Uri($"{ServicesConfig.UrlServidorPorDefecto}/")));
    }

    [Test]
    public void AddClienteWpf_SustituyeElAlmacenamientoDelNavegadorPorElDeFichero()
    {
        // Arrange
        var config = CrearConfiguracion("""{ "Chat": { "Canales": [ "#general" ] } }""");
        var servicios = new ServiceCollection();

        // Act
        servicios.AddClienteWpf(config);
        using var proveedor = servicios.BuildServiceProvider();

        // Assert: la RCL registra el LocalStorageService (JS de navegador),
        // pero el escritorio lo SUSTITUYE por el de fichero — sin esto, la
        // sesión reventaría con "Cannot invoke JavaScript outside of a
        // WebView context" (la antigua barra roja al entrar en el chat)
        Assert.That(proveedor.GetRequiredService<ILocalStorageService>(),
            Is.TypeOf<FileStorageService>());
    }

    #endregion
}
