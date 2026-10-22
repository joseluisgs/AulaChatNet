using AulaChatNet.Client.Blazor.Components.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.IO;        // File, Directory, Path (la variante windows del SDK no los trae implícitos)
using System.Text.Json;
using System.Threading; // SemaphoreSlim

namespace AulaChatNet.Client.Wpf.Services;

/// <inheritdoc cref="ILocalStorageService" />
///
/// <remarks>
/// 🎓 La web guarda la sesión en el <c>localStorage</c> del navegador vía
/// JSInterop; el escritorio NO puede hacer lo mismo. BlazorWebView solo
/// cablea el <c>IJSRuntime</c> de los COMPONENTES, y una store singleton que
/// lo use recibe una instancia sin cablear y revienta con
/// <c>Cannot invoke JavaScript outside of a WebView context</c> (eso era la
/// barra roja al entrar en el chat). Además, un .exe no tiene "navegador":
/// su almacenamiento local es un FICHERO en <c>%LOCALAPPDATA%</c>.
///
/// Misma interfaz (<see cref="ILocalStorageService"/>) → la
/// <c>SesionStore</c> de la librería compartida no cambia ni se entera de
/// qué hay debajo: solo el host WPF cambia el registro. Esa es la gracia de
/// tener la abstracción.
///
/// El fichero es UN JSON tipo diccionario <c>{ "clave": valor }</c>
/// (<c>almacenamiento.json</c>): la misma idea que el localStorage, pero
/// en el disco de este equipo.
/// </remarks>
/// <param name="carpeta">
/// Carpeta base donde vive el fichero (por defecto, %LOCALAPPDATA%). Los
/// tests pasan una carpeta temporal: así prueban el servicio real sin
/// ensuciar el equipo del que esté ejecutando.
/// </param>
/// <param name="logger">Logger opcional (la app registra Serilog).</param>
public class FileStorageService(string? carpeta = null, ILogger<FileStorageService>? logger = null) : ILocalStorageService
{
    /// <summary>
    /// Ruta del fichero. En DatosLocales y no en Roaming: al igual que los
    /// logs, los datos de la app no deben viajar entre equipos del mismo
    /// perfil (equivalente de escritorio a "este navegador concreto").
    /// </summary>
    private readonly string _ruta = Path.Combine(
        carpeta ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AulaChatNet", "almacenamiento.json");

    /// <summary>Opciones del JSON: indiferentes a las mayúsculas, mismas que
    /// el <c>LocalStorageService</c> del navegador.</summary>
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Guardar-lee-modificar-escribe sobre un único fichero: si llegan dos
    /// operaciones a la vez (p. ej. login + guardado de sesión), esta
    /// "puerta" asíncrona las serializa para que no se pisen. Vive lo mismo
    /// que la store (singleton de proceso), así que no hace falta disponerla.
    /// </summary>
    private readonly SemaphoreSlim _puerta = new(1, 1);

    /// <summary>Logger efectivo (Serilog en la app, NullLogger en los tests).</summary>
    private readonly ILogger<FileStorageService> _log = logger ?? NullLogger<FileStorageService>.Instance;

    /// <inheritdoc cref="ILocalStorageService.SetItemAsync{T}(string, T)" />
    public async Task SetItemAsync<T>(string key, T value)
    {
        await _puerta.WaitAsync();

        try
        {
            var datos = await LeerTodoAsync();
            datos[key] = JsonSerializer.SerializeToElement(value, Opciones);
            await EscribirTodoAsync(datos);
        }
        finally
        {
            // Liberar SIEMPRE, aunque falle el disco: si no, la puerta
            // quedaría cerrada para siempre y toda la sesión se bloquearía
            _puerta.Release();
        }
    }

    /// <inheritdoc cref="ILocalStorageService.GetItemAsync{T}(string)" />
    public async Task<T?> GetItemAsync<T>(string key)
    {
        await _puerta.WaitAsync();

        try
        {
            var datos = await LeerTodoAsync();

            if (!datos.TryGetValue(key, out var elemento))
                return default;

            try
            {
                return elemento.Deserialize<T>(Opciones);
            }
            catch (JsonException)
            {
                // Un valor tocado a mano (o escrito por otra versión) no puede
                // romper nada: se responde "no hay nada" (misma política que
                // el servicio del navegador)
                return default;
            }
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc cref="ILocalStorageService.RemoveItemAsync(string)" />
    public async Task RemoveItemAsync(string key)
    {
        await _puerta.WaitAsync();

        try
        {
            var datos = await LeerTodoAsync();

            // Solo reescribir si había algo que borrar (evita tocar el
            // disco por cada "Olvidar sesión" repetido)
            if (datos.Remove(key))
                await EscribirTodoAsync(datos);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc cref="ILocalStorageService.ClearAsync" />
    public async Task ClearAsync()
    {
        await _puerta.WaitAsync();

        try
        {
            await EscribirTodoAsync(new Dictionary<string, JsonElement>());
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>
    /// Lee el fichero entero como diccionario. Fichero ausente (primer
    /// arranque) o JSON corrupto = diccionario vacío: la app arranca aunque
    /// alguien haya tocado el fichero a mano; en el próximo guardado se
    /// reescribe limpio. Un fallo de disco SÍ sube: la política de "nunca
    /// romper por la sesión" vive en <c>SesionStore</c>, que ya lo pilla.
    /// </summary>
    /// <returns>Diccionario clave → valor.</returns>
    private async Task<Dictionary<string, JsonElement>> LeerTodoAsync()
    {
        try
        {
            if (!File.Exists(_ruta))
                return new Dictionary<string, JsonElement>();

            var json = await File.ReadAllTextAsync(_ruta);

            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Opciones)
                ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            _log.LogWarning("El fichero de almacenamiento local está corrupto; se empieza de cero: {Fichero}",
                _ruta);
            return new Dictionary<string, JsonElement>();
        }
    }

    /// <summary>
    /// Escribe el diccionario completo. Se escribe en un temporal y se
    /// renombra encima (escritura "atómica"): si la luz se corta a mitad,
    /// queda el fichero anterior intacto en lugar de medio JSON roto.
    /// </summary>
    /// <param name="datos">Diccionario completo a persistir.</param>
    private async Task EscribirTodoAsync(Dictionary<string, JsonElement> datos)
    {
        // Crear la carpeta si no existe (primer arranque)
        Directory.CreateDirectory(Path.GetDirectoryName(_ruta)!);

        var json = JsonSerializer.Serialize(datos, Opciones);
        var temporal = $"{_ruta}.tmp";

        await File.WriteAllTextAsync(temporal, json);
        File.Move(temporal, _ruta, overwrite: true);
    }
}
