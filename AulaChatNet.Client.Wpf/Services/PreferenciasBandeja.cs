using AulaChatNet.Client.Blazor.Components.Services.Storage;   // ILocalStorageService

namespace AulaChatNet.Client.Wpf.Services;

/// <summary>
/// Preferencias de la bandeja del sistema. De momento guarda UN solo dato:
/// si ya se enseñó el aviso de "la app sigue en segundo plano", para no
/// repetirlo cada vez que se oculta la ventana. Se persiste en el MISMO
/// fichero que la sesión del usuario (misma <c>ILocalStorageService</c>), y
/// al ser lógica pura (sin WPF) se puede probar sin crear ventanas.
/// </summary>
public class PreferenciasBandeja(ILocalStorageService almacen)
{
    /// <summary>
    /// Clave bajo la que queda registrado en el fichero que el aviso ya se
    /// enseñó alguna vez.
    /// </summary>
    public const string ClaveAvisoMostrado = "aulachat:bandeja:aviso-mostrado";

    /// <summary>
    /// Dice si el aviso de segundo plano todavía no se ha enseñado nunca.
    /// </summary>
    /// <returns><c>true</c> la primera vez; después, <c>false</c>.</returns>
    public async Task<bool> EsPrimeraVezAsync()
    {
        // Si la clave no existe, el almacenamiento devuelve el valor por
        // defecto del tipo (false) — es decir, "nunca visto" → primera vez
        var visto = await almacen.GetItemAsync<bool>(ClaveAvisoMostrado);
        return !visto;
    }

    /// <summary>
    /// Marca el aviso como enseñado para no volver a mostrarlo.
    /// </summary>
    /// <returns>Tarea que se completa al escribir en el fichero.</returns>
    public Task MarcarAvisoMostradoAsync() =>
        almacen.SetItemAsync(ClaveAvisoMostrado, true);
}
