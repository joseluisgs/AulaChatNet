using AulaChatNet.Client.Web.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace AulaChatNet.Client.Web.State;

/// <inheritdoc cref="ISesionStore" />
///
/// <remarks>
/// Esta store decide la POLÍTICA de la sesión (qué recordamos y qué pasa si
/// el almacenamiento no deja escribir); el trabajo sucio vive en
/// <see cref="ILocalStorageService"/>: en la web, localStorage vía JS; en
/// el escritorio, un fichero (la implementación la elige cada host).
///
/// Un chat nunca puede romperse por una sesión ilegible: todos los fallos
/// responden "no hay sesión" en vez de lanzar excepciones al usuario.
/// </remarks>
/// <param name="almacenamiento">Almacenamiento local de la sesión (JS en web,
/// fichero en escritorio: lo decide cada host).</param>
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
            _log.LogWarning("No se pudo leer la sesión guardada: {Motivo}",
                ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            // Política de la store: NUNCA romper por una sesión ilegible
            // (en escritorio: disco lleno, fichero bloqueado...). Ojo: si
            // esta excepción subiera, terminaría en la barra roja de error
            // de Blazor
            _log.LogWarning(ex, "No se pudo leer la sesión guardada; se continúa sin ella");
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
            _log.LogWarning("No se pudo guardar la sesión: {Motivo}", ex.Message);
        }
        catch (Exception ex)
        {
            // Misma política: el "recuerdo" es opcional, el chat no puede
            // caerse por él. Antes de arreglar el escritorio, aquí caía un
            // InvalidOperationException de JSInterop y era LA causa de la
            // barra roja al entrar en el chat
            _log.LogWarning(ex, "No se pudo guardar la sesión; se continúa sin recordarla");
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
            _log.LogWarning("No se pudo borrar la sesión: {Motivo}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo borrar la sesión guardada");
            return false;
        }
    }
}
