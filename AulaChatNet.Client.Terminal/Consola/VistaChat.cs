using System.Text;
using AulaChatNet.Shared.Dtos;
using AulaChatNet.Shared.Helpers;
using Spectre.Console;

namespace AulaChatNet.Client.Terminal.Consola;

/// <summary>
/// Fórmulas de markup de la zona de mensajes: cómo se PINTA una fila —del
/// canal o privada— en la consola.
///
/// <remarks>
/// Está separada de Program.cs por la MISMA razón que <see cref="Conectados"/>:
/// siendo estática y pura —solo datos de entrada, cadena de salida— se testea
/// sin red, sin consola y sin abrir la ventana, que es lo que hace
/// <c>VistaChatTests</c>. Además concentra aquí lo que los DOS modos de
/// entrada (lienzo en vivo y tubería de pruebas) necesitan compartir: si el
/// pintado viviera en dos sitios, la terminal enseñaría mensajes privados
/// distintos según cómo se hubiera arrancado.
///
/// QUÉ pinta cada fila y EN QUÉ ORDEN van las filas son DOS preguntas
/// distintas: el orden lo resuelve <see cref="CombinadorChat"/> (Shared,
/// idéntico para la terminal y para la web) y aquí solo se pinta lo que
/// llegue ya ordenado.
/// </remarks>
/// </summary>
public static class VistaChat
{
    /// <summary>Pinta una fila de la vista, sea del canal o privada.</summary>
    /// <param name="fila">Fila ya ordenada por <see cref="CombinadorChat"/>.</param>
    /// <param name="yo">Mi propio nombre: decide si el privado se pinta como "para" o como "de".</param>
    /// <param name="reacciones">Reacciones por mensaje del canal (null en modo tubería o sin chips).</param>
    /// <returns>Cadena de markup lista para <c>Markup</c>.</returns>
    public static string LineaMarkup(
        FilaChat fila, string yo, IReadOnlyDictionary<Guid, ReaccionesMensajeDto>? reacciones = null) =>
        fila.EsPrivado
            ? LineaPrivadoMarkup(fila.Privado!, yo)
            : LineaMensajeMarkup(fila.Mensaje!, reacciones);

    /// <summary>
    /// Una fila del canal: hora, iniciales entre corchetes y nombre con su
    /// color derivado; los mensajes de sistema van en gris cursiva.
    ///
    /// <remarks>
    /// El servidor marca en UTC (la regla de oro: guardar UTC, convertir SOLO
    /// al presentar), así que aquí se pasa a la hora local del que mira: sin
    /// esto, la terminal enseñaría otra hora. Los mensajes de sistema TAMBIÉN
    /// llevan hora — en un aula se quiere saber cuándo se fue alguien, y
    /// Discord y Slack lo hacen igual.
    /// </remarks>
    /// </summary>
    /// <param name="mensaje">Mensaje del canal.</param>
    /// <param name="reacciones">Reacciones por mensaje (null = sin chips).</param>
    /// <returns>Cadena de markup lista para pintar.</returns>
    public static string LineaMensajeMarkup(
        MensajeDto mensaje, IReadOnlyDictionary<Guid, ReaccionesMensajeDto>? reacciones = null)
    {
        var hora = $"{mensaje.Timestamp.ToLocalTime():HH:mm}";

        if (mensaje.Tipo == TipoMensaje.Sistema)
            return $"[grey]{hora}[/] [grey italic]· {Markup.Escape(mensaje.Texto)}[/]";

        var hex = TemaDiscord.ColorDe(mensaje.Usuario).ToMarkup();
        var iniciales = TemaDiscord.Iniciales(mensaje.Usuario);

        return $"[grey]{hora}[/] " +
               $"[{hex}][[{iniciales}]][/] " +
               $"[{hex}]{Markup.Escape(mensaje.Usuario)}[/]: {TextoConMencionesMarkup(mensaje.Texto)}" +
               ChipsReaccionesMarkup(mensaje, reacciones);
    }

    /// <summary>
    /// El texto con las menciones (@ana) resaltadas en amarillo: la misma
    /// regla de troceado que <see cref="Menciones"/> (Shared) ofrece a los
    /// dos clientes, aquí montada en markup de Spectre. Cada tramo se
    /// ESCAPA ANTES de teñirlo, así que un nombre con corchetes no se come
    /// el formato de la línea entera.
    /// </summary>
    /// <param name="texto">Texto del mensaje ya validado.</param>
    /// <returns>Cadena de markup con las menciones resaltadas.</returns>
    private static string TextoConMencionesMarkup(string texto)
    {
        var amarillo = TemaDiscord.Amarillo.ToMarkup();
        var sb = new StringBuilder();

        foreach (var (trozo, esMencion) in Menciones.Trocear(texto))
            sb.Append(esMencion
                ? $"[{amarillo}]{Markup.Escape(trozo)}[/]"
                : Markup.Escape(trozo));

        return sb.ToString();
    }

    /// <summary>
    /// Los chips de reacción al final de la línea: gris y con la frase de
    /// Shared (contador y quiénes), escapada — un nombre con corchetes no
    /// debe comerse el formato de la línea entera. Vacío si el mensaje no
    /// tiene reacciones o es de sistema (a esos no se les reacciona).
    /// </summary>
    /// <param name="mensaje">Mensaje al que mirar sus reacciones.</param>
    /// <param name="reacciones">Reacciones por mensaje del canal.</param>
    /// <returns>Cadena de markup (con el espacio que la precede) o vacía.</returns>
    private static string ChipsReaccionesMarkup(
        MensajeDto mensaje, IReadOnlyDictionary<Guid, ReaccionesMensajeDto>? reacciones)
    {
        if (mensaje.Tipo == TipoMensaje.Sistema
            || reacciones is null
            || !reacciones.TryGetValue(mensaje.Id, out var estado))
        {
            return string.Empty;
        }

        var frase = ReaccionesTexto.Descripcion(estado.Reacciones);

        return frase.Length == 0
            ? string.Empty
            : $" [grey]{Markup.Escape(frase)}[/]";
    }

    /// <summary>
    /// Una fila PRIVADA: misma estructura que un mensaje normal —para que se
    /// lea igual— con el 🔒 en rosa y el contexto "para X" o "de X" según
    /// quién lo escribió. Así se distingue a la primera sin necesidad de
    /// cambiar el color del autor ni añadir una segunda columna.
    /// </summary>
    /// <param name="privado">Mensaje privado.</param>
    /// <param name="yo">Mi nombre: si coincide con el emisor, es "para"; si no, es "de".</param>
    /// <returns>Cadena de markup lista para pintar.</returns>
    public static string LineaPrivadoMarkup(MensajePrivadoDto privado, string yo)
    {
        var hora = $"{privado.Timestamp.ToLocalTime():HH:mm}";

        // El emisor es SIEMPRE el que pinta el nombre; el contexto cambia:
        // "para Luis" si lo escribí yo, "de Ana" si me lo enviaron
        var esMio = string.Equals(privado.Emisor, yo, StringComparison.OrdinalIgnoreCase);
        var contexto = esMio ? $"para {privado.Destinatario}" : $"de {privado.Emisor}";

        var hex = TemaDiscord.ColorDe(privado.Emisor).ToMarkup();
        var iniciales = TemaDiscord.Iniciales(privado.Emisor);
        var rosa = TemaDiscord.Fucsia.ToMarkup();

        return $"[grey]{hora}[/] [{rosa}🔒[/] [{rosa}]{Markup.Escape(contexto)}[/] " +
               $"[{hex}][[{iniciales}]][/] [{hex}]{Markup.Escape(privado.Emisor)}[/]: " +
               $"{Markup.Escape(privado.Texto)}";
    }

    /// <summary>
    /// La línea de "está escribiendo...": gris y sin adornos, montada sobre
    /// la frase de Shared para que la terminal y la web digan EXACTAMENTE lo
    /// mismo. El escapado cubre nombres con corchetes, que Spectre trataría
    /// como formato y teñirían la línea de un color que nadie eligió.
    /// </summary>
    /// <param name="escribiendo">Nombres activos (el orden lo pone el seguimiento).</param>
    /// <returns>Cadena de markup; vacía si no hay nadie, para no pintar una fila en balde.</returns>
    public static string LineaEscribiendoMarkup(IReadOnlyList<string> escribiendo)
    {
        var frase = EscribiendoTexto.Descripcion(escribiendo);

        return frase.Length == 0 ? string.Empty : $"[grey]{Markup.Escape(frase)}[/]";
    }
}
