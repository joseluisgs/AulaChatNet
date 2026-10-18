using AulaChatNet.Client.Blazor.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace AulaChatNet.Client.Blazor.State;

/// <inheritdoc cref="ISesionStore" />
///
/// <remarks>
/// Esta store decide la POLÍTICA de la sesión (qué recordamos y qué pasa si
/// el navegador no deja escribir); el trabajo sucio con JS y JSON vive en
/// <see cref="ILocalStorageService"/> (calco del servicio de la Tienda).
///
/// Un chat nunca puede romperse por una sesión ilegible: todos los fallos
/// responden "no hay sesión" en vez de lanzar excepciones al usuario.
/// </remarks>
/// <param name="almacenamiento">Almacenamiento local del navegador.</param>
/// <param name="logger">
/// Logger opcional (la app registra Serilog → consola del navegador). Sin él
/// (tests sin DI) se usa NullLogger: registrar un fallo de localStorage nunca
/// debe ser motivo para que reviente la store.
/// </param>
public class SesionStore(ILocalStorageService almacenamiento, ILogger<SesionStore>? logger = null)
    : ISesionStore
{
    /// <summary>Clave del elemento en localStorage (una por aplicación).</summary>
    private const string Clave = "aulachat.sesion";

    /// <summary>Logger efectivo (Serilog en la app, NullLogger en los tests).</summary>
    private readonly ILogger<SesionStore> _log = logger ?? NullLogger<SesionStore>.Instance;

    /// <inheritdoc />
    public async Task<SesionGuardada?> CargarAsync()
    {
        try
        {
            return await almacenamiento.GetItemAsync<SesionGuardada>(Clave);
        }
        catch (JSException ex)
        {
            // Navegador en modo privado o almacenamiento bloqueado:
            // no hay sesión y volvemos a preguntar. En pantalla se ignora
            // (es política de la store); el log sí lo registra
            _log.LogWarning("No se pudo leer la sesión de localStorage (¿bloqueado?): {Motivo}",
                ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task GuardarAsync(SesionGuardada sesion)
    {
        try
        {
            await almacenamiento.SetItemAsync(Clave, sesion);
        }
        catch (JSException ex)
        {
            // Sin permiso de escritura (modo incógnito): la app sigue,
            // solo no recordará nada para la próxima
            _log.LogWarning("No se pudo guardar la sesión en localStorage: {Motivo}", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> BorrarAsync()
    {
        try
        {
            var sesion = await almacenamiento.GetItemAsync<SesionGuardada>(Clave);

            if (sesion is null)
                return false;

            await almacenamiento.RemoveItemAsync(Clave);

            return true;
        }
        catch (JSException ex)
        {
            _log.LogWarning("No se pudo borrar la sesión de localStorage: {Motivo}", ex.Message);
            return false;
        }
    }
}
