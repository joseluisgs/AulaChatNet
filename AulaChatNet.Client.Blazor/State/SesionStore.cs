using AulaChatNet.Client.Blazor.Services.Storage;
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
public class SesionStore(ILocalStorageService almacenamiento) : ISesionStore
{
    /// <summary>Clave del elemento en localStorage (una por aplicación).</summary>
    private const string Clave = "aulachat.sesion";

    /// <inheritdoc />
    public async Task<SesionGuardada?> CargarAsync()
    {
        try
        {
            return await almacenamiento.GetItemAsync<SesionGuardada>(Clave);
        }
        catch (JSException)
        {
            // Navegador en modo privado o almacenamiento bloqueado:
            // no hay sesión y volvemos a preguntar
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
        catch (JSException)
        {
            // Sin permiso de escritura (modo incógnito): la app sigue,
            // solo no recordará nada para la próxima
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
        catch (JSException)
        {
            return false;
        }
    }
}
