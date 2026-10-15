using System.Text;
using Spectre.Console;

namespace AulaChatNet.Client.Terminal.Consola;

/// <summary>
/// Línea de entrada con cursor, como el editor de cualquier chat: se puede
/// escribir al final, moverse con las flechas, borrar en medio (Retroceso
/// y Supr), saltar a Inicio/Fin... Es lógica PURA —sin consola ni teclado—
/// por eso se testea sin abrir ninguna ventana.
///
/// <remarks>
/// La vista en vivo solo traduce teclas a estos métodos y pinta lo que la
/// clase devuelve: la pantalla jamás calcula nada, solo refleja el estado
/// del editor. El cursor se pinta como un carácter resaltado (fondo claro)
/// en lugar de mover el cursor real de la terminal, que es el truco para
/// que AnsiConsole.Live pueda repintar el lienzo entero sin pelearse con
/// la posición del cursor del SO.
/// </remarks>
/// </summary>
public class EditorLinea
{
    private readonly StringBuilder _texto = new();

    /// <summary>Posición del cursor dentro del texto (0 = inicio).</summary>
    public int Posicion { get; private set; }

    /// <summary>Texto escrito hasta ahora.</summary>
    public string Texto => _texto.ToString();

    /// <summary>Largo del texto.</summary>
    public int Longitud => _texto.Length;

    /// <summary>Añade un carácter en la posición del cursor y lo avanza.</summary>
    /// <param name="c">Carácter tecleado.</param>
    public void Insertar(char c)
    {
        _texto.Insert(Posicion, c);
        Posicion++;
    }

    /// <summary>Borra el carácter anterior al cursor (tecla Retroceso).</summary>
    public void BorrarAtras()
    {
        if (Posicion == 0)
            return;

        _texto.Remove(Posicion - 1, 1);
        Posicion--;
    }

    /// <summary>Borra el carácter bajo el cursor (tecla Supr).</summary>
    public void BorrarDelante()
    {
        if (Posicion >= _texto.Length)
            return;

        _texto.Remove(Posicion, 1);
    }

    /// <summary>Mueve el cursor una posición a la izquierda (flecha ←).</summary>
    public void MoverIzquierda()
    {
        if (Posicion > 0)
            Posicion--;
    }

    /// <summary>Mueve el cursor una posición a la derecha (flecha →).</summary>
    public void MoverDerecha()
    {
        if (Posicion < _texto.Length)
            Posicion++;
    }

    /// <summary>Lleva el cursor al principio (tecla Inicio).</summary>
    public void Inicio() => Posicion = 0;

    /// <summary>Lleva el cursor al final (tecla Fin).</summary>
    public void Fin() => Posicion = _texto.Length;

    /// <summary>Vacía la línea y devuelve el cursor al inicio (Escape o envío).</summary>
    public void Limpiar()
    {
        _texto.Clear();
        Posicion = 0;
    }

    /// <summary>
    /// El texto como markup listo para pintar, con el cursor VISIBLE como
    /// carácter resaltado: el propio carácter que hay debajo, o un espacio
    /// si el cursor está al final. Los corchetes del texto se escapan
    /// (si no, Spectre los leería como markup y estallaría el pintado).
    /// </summary>
    /// <returns>Markup de "texto + cursor" sin el prefijo del prompt.</returns>
    public string ParaPintar()
    {
        var texto = _texto.ToString();

        var antes = Markup.Escape(texto[..Posicion]);

        // Después del cursor: el resto SIN el carácter del cursor (que ya se
        // pinta resaltado); al final, no hay después
        var despues = Markup.Escape(
            Posicion < texto.Length ? texto[(Posicion + 1)..] : string.Empty);

        var cursor = Posicion < _texto.Length
            ? $"[black on white]{Markup.Escape(_texto[Posicion].ToString())}[/]"
            : "[black on white] [/]";

        return $"{antes}{cursor}{despues}";
    }
}
