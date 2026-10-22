using AulaChatNet.Client.Web.State;
using AulaChatNet.Shared.Comandos;
using AulaChatNet.Shared.Configuracion;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;

namespace AulaChatNet.Client.Web.Services;

/// <inheritdoc cref="IProcesadorComandos" />
///
/// <remarks>
/// Es la contraparte web de <c>ProcesarLinea</c> de la terminal: MISMO
/// parser y MISMAS reglas de validación (ChatConfig compartida), pero la
/// salida va al flujo de mensajes de la store en lugar de a la consola.
/// Añadir un comando nuevo es tocar el parser (Shared), el catálogo de
/// ayuda (Shared) y ESTE switch — la terminal toca su propio switch.
/// </remarks>
/// <param name="store">Store de chat (estado reactivo y conexión al hub).</param>
/// <param name="config">Configuración compartida (canales y límites).</param>
/// <param name="sesion">Sesión guardada en el navegador (localStorage).</param>
public class ProcesadorComandos(IChatStore store, ChatConfig config, ISesionStore sesion)
    : IProcesadorComandos
{
    /// <inheritdoc />
    public async Task EjecutarAsync(Comando comando)
    {
        switch (comando)
        {
            case Comando.Enviar:
                // Un mensaje no pasa por aquí: la caja de texto lo valida y
                // lo envía por su propio camino (errores de campo junto al
                // campo, errores de conexión en el flujo)
                break;

            case Comando.Ayuda { Tema: var tema }:
                store.Aviso(CrearAyuda(tema));
                break;

            case Comando.Canales:
                store.Aviso(CrearTextoCanales());
                break;

            case Comando.Canal { Nombre: var nombreCanal }:
                // Validación local ANTES de tocar la red, mismo criterio
                // que el servidor (ChatConfig lo comparten los dos)
                if (!config.EsCanalValido(nombreCanal))
                {
                    store.Aviso(config.ErrorCanalDesconocido(nombreCanal));
                    break;
                }

                var errorCanal = await store.CambiarCanalAsync(nombreCanal);

                // Si tuvo éxito no se avisa nada: el historial del nuevo
                // canal llega por evento y cambia la vista solo
                if (errorCanal is not null)
                    store.Aviso(errorCanal);

                break;

            case Comando.Nick { Nombre: var nombreNick }:
                var errorNick = config.ValidarUsuario(nombreNick.Trim()) is { } fallo
                    ? fallo
                    : await store.CambiarNickAsync(nombreNick.Trim());

                // El aviso "X ahora se llama Y" lo pinta el servidor por el
                // flujo normal; aquí solo salen los ERRORES
                if (errorNick is not null)
                    store.Aviso(errorNick);

                break;

            case Comando.Usuarios { NombreCanal: var canalConsulta }:
                store.Aviso(await CrearTextoUsuariosAsync(canalConsulta));
                break;

            case Comando.Privado { Destinatario: var destinatario, Texto: var textoPrivado }:
                // El eco del propio mensaje vuelve por evento (privado
                // recibido) y lo pinta la vista junto al resto: aquí solo
                // se traducen los ERRORES, igual que en el resto de comandos
                var errorPrivado = await store.EnviarPrivadoAsync(
                    destinatario.Trim(), textoPrivado.Trim());

                if (errorPrivado is not null)
                    store.Aviso(errorPrivado);

                break;

            case Comando.Reaccion { Emoji: var emoji }:
                var errorReaccion = await ReaccionarAsync(emoji);

                // Sin error no se dice nada: el chip aparece solo cuando el
                // servidor difunde el estado del mensaje
                if (errorReaccion is not null)
                    store.Aviso(errorReaccion);

                break;

            case Comando.Editar { Texto: var textoEdicion }:
                // El eco vuelve por MENSAJE EDITADO (la fila se repinta con
                // el texto nuevo y su marca): aquí solo salen los ERRORES
                var errorEdicion = await EditarUltimoPropioAsync(textoEdicion);

                if (errorEdicion is not null)
                    store.Aviso(errorEdicion);

                break;

            case Comando.Borrar:
                // El eco vuelve por MENSAJE BORRADO (la fila desaparece):
                // mismos criterios que editar, con el Id como única pista
                var errorBorrado = await BorrarUltimoPropioAsync();

                if (errorBorrado is not null)
                    store.Aviso(errorBorrado);

                break;

            case Comando.Reconectar:
                store.Aviso(await CrearTextoReconectarAsync());
                break;

            case Comando.Sesion { Accion: var accion }:
                store.Aviso(accion == AccionSesion.Borrar
                    ? await CrearTextoBorrarSesionAsync()
                    : await CrearTextoVerSesionAsync());
                break;

            case Comando.Limpiar:
                // Silencioso, igual que en la terminal: no hay nada que
                // comentar cuando solo se limpia la vista
                store.LimpiarVista();
                break;

            case Comando.Salir:
                // Mismo camino que el botón Salir de la barra: cierre
                // ordenado y la página vuelve al panel de entrada
                await store.CerrarAsync();
                break;

            case Comando.Desconocido { Palabra: var palabra }:
                store.Aviso($"Comando no reconocido: {palabra}. Escribe /ayuda para ver todos.");
                break;

            case Comando.Invalido invalido:
                store.Aviso($"{invalido.Motivo}\n{invalido.Uso}");
                break;
        }
    }

    /// <summary>
    /// /reaccion emoji: sobre el ÚLTIMO mensaje de una persona del canal
    /// (mismo criterio que la terminal). El emoji se valida contra la lista
    /// cerrada ANTES de tocar la red — el servidor vuelve a validar, porque
    /// de la red llega lo que quiera.
    /// </summary>
    /// <param name="emoji">Emoji escrito por el usuario.</param>
    /// <returns>null si el alterna salió bien; el error, si no.</returns>
    private async Task<string?> ReaccionarAsync(string emoji)
    {
        var canonico = EmojisReaccion.Canonico(emoji);

        if (canonico is null)
            return $"Emoji no admitido. Usa: {EmojisReaccion.ListaLegible}";

        // Solo mensajes de PERSONA: a los avisos de sistema no se les reacciona
        var ultimo = store.VistaMensajes.LastOrDefault(m => m.Tipo == TipoMensaje.Normal);

        if (ultimo is null)
            return "No hay mensajes que reaccionar en este canal.";

        return await store.ReaccionarAsync(ultimo.Id, canonico);
    }

    /// <summary>
    /// /editar texto por COMANDO (la web además tiene botones sobre
    /// cualquier mensaje propio): el objetivo es TU Último mensaje del
    /// canal, el mismo criterio estrechado de /reaccion. El servidor vuelve
    /// a validar la propiedad y el texto: aquí solo se elige QUÉ mensaje.
    /// </summary>
    /// <param name="texto">Nuevo texto escrito por el usuario.</param>
    /// <returns>null si la edición salió bien; el error, si no.</returns>
    private async Task<string?> EditarUltimoPropioAsync(string texto)
    {
        var mio = UltimoMensajePropio();

        if (mio is null)
            return "No hay ningún mensaje tuyo que editar en este canal.";

        return await store.EditarMensajeAsync(mio.Id, texto);
    }

    /// <summary>
    /// /borrar por COMANDO: mismo objetivo que /editar —tu último mensaje
    /// propio— y mismo eco por evento. Sin confirmación previa a
    /// propósito: el gesto ya es explícito (los botones 🗑️ de la web sí
    /// confirman, porque allí el ratón tropieza con más facilidad).
    /// </summary>
    /// <returns>null si el borrado salió bien; el error, si no.</returns>
    private async Task<string?> BorrarUltimoPropioAsync()
    {
        var mio = UltimoMensajePropio();

        if (mio is null)
            return "No hay ningún mensaje tuyo que borrar en este canal.";

        return await store.BorrarMensajeAsync(mio.Id);
    }

    /// <summary>
    /// Tu último mensaje de PERSONA en el canal actual: los avisos de
    /// sistema no son tuyos aunque salgan con nombre de la casa, y la
    /// comparación es por nick ignorando mayúsculas (mismo criterio que la
    /// tienda de identidad del servidor).
    /// </summary>
    /// <returns>El mensaje, o null si todavía no has escrito nada en el canal.</returns>
    private MensajeDto? UltimoMensajePropio() =>
        store.VistaMensajes.LastOrDefault(m =>
            m.Tipo == TipoMensaje.Normal
            && string.Equals(m.Usuario, store.VistaNick, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ayuda como TEXTO (la terminal la pinta como tabla de Spectre): una
    /// línea por comando, a partir del catálogo compartido. El texto lleva
    /// saltos de línea y el CSS del mensaje ya usa pre-wrap, así que se
    /// respeta tal cual.
    /// </summary>
    /// <param name="tema">Comando del que se pide ayuda (null = general).</param>
    /// <returns>Ayuda lista para mostrar como aviso de sistema.</returns>
    private static string CrearAyuda(string? tema)
    {
        var encontrados = AyudaComandos.Buscar(tema);

        if (encontrados.Count == 0)
            return $"No hay ayuda para '{tema}'. Escribe /ayuda para ver todos los comandos.";

        var filas = encontrados.Select(c => $"{c.Sintaxis} — {c.Descripcion}");

        return tema is null
            ? "Comandos disponibles:\n" + string.Join("\n", filas)
            : string.Join("\n", filas);
    }

    /// <summary>
    /// /canales: lista de canales con sus conteos, marcando el actual.
    /// Mismo contenido que pinta la terminal, en texto plano.
    /// </summary>
    private string CrearTextoCanales()
    {
        var est = store.EstadisticasActuales;

        var porCanal = est.Canales
            .ToDictionary(c => c.Canal, c => c.Conectados, StringComparer.OrdinalIgnoreCase);

        var filas = new List<string> { $"📌 {est.TotalConectados} conectados en total:" };

        foreach (var canalDisponible in store.Canales)
        {
            porCanal.TryGetValue(canalDisponible, out var conectados);
            var esActual = string.Equals(canalDisponible, store.Canal, StringComparison.OrdinalIgnoreCase);

            filas.Add($"  {canalDisponible} {conectados}{(esActual ? " ● actual" : string.Empty)}");
        }

        return string.Join("\n", filas);
    }

    /// <summary>
    /// /usuarios [canal]: sin argumento, los de TU canal con la lista local
    /// (mismo dato, sin red); con argumento, se consulta el servidor para
    /// mirar dentro de otra sala sin cambiar de canal.
    /// </summary>
    /// <param name="canalConsulta">Canal escrito por el usuario (null = el actual).</param>
    /// <returns>Texto listo para mostrar como aviso.</returns>
    private async Task<string> CrearTextoUsuariosAsync(string? canalConsulta)
    {
        if (string.IsNullOrWhiteSpace(canalConsulta))
            return CrearTextoListaUsuarios(store.Canal ?? string.Empty, store.VistaUsuarios);

        var canonico = config.EncontrarCanal(canalConsulta);

        if (canonico is null)
            return config.ErrorCanalDesconocido(canalConsulta.Trim());

        if (string.Equals(canonico, store.Canal, StringComparison.OrdinalIgnoreCase))
            return CrearTextoListaUsuarios(canonico, store.VistaUsuarios);

        var usuarios = await store.ObtenerUsuariosAsync(canonico);

        // null = canal desconocido O no se pudo consultar (conexión caída):
        // el mismo mensaje sirve para los dos casos sin mentir en ninguno
        return usuarios is null
            ? "No pude consultar la lista de ese canal."
            : CrearTextoListaUsuarios(canonico, usuarios);
    }

    /// <summary>Lista de usuarios con su cuenta (compartida por /usuarios y /usuarios #canal).</summary>
    /// <param name="canal">Canal cuya lista se pinta.</param>
    /// <param name="usuarios">Conectados de ese canal.</param>
    /// <returns>Texto con la cabecera y un usuario por línea.</returns>
    private static string CrearTextoListaUsuarios(string canal, IReadOnlyList<UsuarioConectadoDto> usuarios) =>
        usuarios.Count == 0
            ? $"📌 Aún no hay nadie conectado en {canal}."
            : $"📌 {usuarios.Count} en línea en {canal}:\n"
              + string.Join("\n", usuarios.Select(u => $"  ● {u.Usuario}"));

    /// <summary>
    /// /reconectar: mismo trinomio que la terminal — si estamos fuera se
    /// intenta recuperar la sesión; si ya se está dentro, se avisa; y si la
    /// reconexión automática aún va en marcha, se pide paciencia.
    /// </summary>
    private async Task<string> CrearTextoReconectarAsync()
    {
        if (store.EstadoValor == EstadoConexion.Desconectado)
        {
            var error = await store.ReconectarAsync();

            return error is null
                ? "● Reconectado: sesión recuperada."
                : error;
        }

        if (store.EstadoValor == EstadoConexion.Conectado)
            return "● Ya estás conectado: no hace falta reconectar.";

        return "⏳ La reconexión automática sigue en marcha: espera unos segundos.";
    }

    /// <summary>/sesion borrar: borra el nick guardado del navegador.</summary>
    private async Task<string> CrearTextoBorrarSesionAsync()
    {
        var borrado = await sesion.BorrarAsync();

        return borrado
            ? "🗑️ Sesión borrada: la próxima vez se preguntará de cero."
            : "🗑️ No había ninguna sesión guardada que borrar.";
    }

    /// <summary>/sesion (sin argumento): enseña lo guardado en el navegador.</summary>
    private async Task<string> CrearTextoVerSesionAsync()
    {
        var guardada = await sesion.CargarAsync();

        return guardada is null || string.IsNullOrWhiteSpace(guardada.Nick)
            ? "No hay ninguna sesión guardada en este navegador."
            : $"Sesión guardada: {guardada.Nick} → {guardada.Canal ?? "(canal por defecto)"}";
    }
}
