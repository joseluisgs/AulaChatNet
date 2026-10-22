using System.Text.Json;
using Microsoft.JSInterop;

namespace AulaChatNet.Client.Blazor.Components.Services.Storage;

/// <inheritdoc cref="ILocalStorageService" />
///
/// <remarks>
/// Calco del <c>LocalStorageService</c> de la Tienda: tres llamadas a
/// <c>localStorage</c> del navegador con JSInterop y JSON de por medio.
/// </remarks>
public class LocalStorageService(IJSRuntime jsRuntime) : ILocalStorageService
{
    /// <summary>Opciones del JSON de la sesion: indiferentes a las mayusculas,
    /// para que un JSON escrito a mano o de otra version siga leyendose.</summary>
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc cref="ILocalStorageService.SetItemAsync{T}(string, T)" />
    public async Task SetItemAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value, Opciones);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, json);
    }

    /// <inheritdoc cref="ILocalStorageService.GetItemAsync{T}(string)" />
    public async Task<T?> GetItemAsync<T>(string key)
    {
        var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);

        if (string.IsNullOrEmpty(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, Opciones);
        }
        catch (JsonException)
        {
            // Un JSON tocado a mano (o de otra versión) no puede romper nada:
            // se responde "no hay nada" y la app vuelve a preguntar
            return default;
        }
    }

    /// <inheritdoc cref="ILocalStorageService.RemoveItemAsync(string)" />
    public async Task RemoveItemAsync(string key) =>
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);

    /// <inheritdoc cref="ILocalStorageService.ClearAsync" />
    public async Task ClearAsync() =>
        await jsRuntime.InvokeVoidAsync("localStorage.clear");
}
