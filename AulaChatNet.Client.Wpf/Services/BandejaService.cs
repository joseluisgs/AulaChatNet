using H.NotifyIcon;                     // TaskbarIcon
using Serilog;
using System.Windows;                   // ResourceDictionary, Style, Thickness
using System.Windows.Controls;          // ContextMenu, MenuItem, Separator
using System.Windows.Controls.Primitives; // PopupAnimation (burbuja propia)
using System.Windows.Media;             // SolidColorBrush, Color
using System.Windows.Media.Imaging;     // BitmapImage

namespace AulaChatNet.Client.Wpf.Services;

/// <summary>
/// Implementación de la bandeja con <c>H.NotifyIcon.Wpf</c>. El icono NO se
/// crea en el arranque: aparece cuando la ventana se oculta por primera vez
/// (mientras la app se ve normal, no ensucia la bandeja) y se retira al
/// salir. El menú contextual se tiñe con la paleta oscura de la app: el
/// menú nativo de Windows es claro y rompería el aspecto de Discord.
/// </summary>
public sealed class BandejaService : IBandejaService
{
    // Paleta Discord congelada (los pincels congelados son más rápidos y se
    // pueden compartir entre hilos)
    private static readonly SolidColorBrush PincelFondo = CrearPincel(0x2f, 0x31, 0x36);
    private static readonly SolidColorBrush PincelTexto = CrearPincel(0xdc, 0xdd, 0xde);
    private static readonly SolidColorBrush PincelBorde = CrearPincel(0x20, 0x22, 0x25);
    private static readonly SolidColorBrush PincelTextoSecundario = CrearPincel(0x8e, 0x92, 0x97);

    /// <summary>El icono en la bandeja; null mientras no se haya mostrado.</summary>
    private TaskbarIcon? _icono;

    /// <inheritdoc/>
    public event EventHandler? AbrirPulsado;

    /// <inheritdoc/>
    public event EventHandler? SalirPulsado;

    /// <inheritdoc/>
    public void Mostrar()
    {
        // Idempotente: ocultar la ventana dos veces seguidas no crea dos iconos
        if (_icono is not null)
        {
            return;
        }

        // El icono es el MISMO maestro que el de la ventana y el del .exe:
        // resources/icon-app.ico está como Resource en el csproj, así que se
        // puede referenciar por su URI de pack (va embebido en el ensamblado)
        _icono = new TaskbarIcon
        {
            ToolTipText = "AulaChatNet",
            IconSource = new BitmapImage(
                new Uri("pack://application:,,,/resources/icon-app.ico", UriKind.Absolute)),
            ContextMenu = CrearMenu(),
        };

        // Clic izquierdo o doble clic = volver a la ventana (como Discord)
        _icono.TrayLeftMouseUp += (_, _) => AbrirPulsado?.Invoke(this, EventArgs.Empty);
        _icono.TrayMouseDoubleClick += (_, _) => AbrirPulsado?.Invoke(this, EventArgs.Empty);

        // Un TaskbarIcon creado desde CÓDIGO no monta el icono nativo hasta
        // que se lo pides (el declarado en XAML lo hace al cargar): sin
        // esto, el icono no aparece y la burbuja revienta con
        // "TrayIcon is not created"
        _icono.ForceCreate();

        Log.Information("Icono de bandeja mostrado: la app sigue en segundo plano");
    }

    /// <inheritdoc/>
    public void AvisarSegundoPlano()
    {
        if (_icono is null)
        {
            return;
        }

        // Burbuja PROPIA en vez de la nativa de Windows: la del sistema sale
        // blanca en Windows 11 (una app sin manifiesto de tema no puede
        // oscurecerla) y rompe el aspecto. Con ShowCustomBalloon el aviso es
        // un control WPF normal: la estética la pone la app, como en todo
        var texto = new StackPanel();
        texto.Children.Add(new TextBlock
        {
            Text = "AulaChatNet",
            Foreground = PincelTexto,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        texto.Children.Add(new TextBlock
        {
            Text = "La app sigue en segundo plano y conectada al aula. " +
                   "Vuelve con doble clic en el icono o con su menú.",
            Foreground = PincelTextoSecundario,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        var burbuja = new Border
        {
            Background = PincelFondo,
            BorderBrush = PincelBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            MaxWidth = 300,
            Child = texto,
        };

        // Se muestra junto al icono de la bandeja y se cierra sola a los 7 s
        _icono.ShowCustomBalloon(burbuja, PopupAnimation.Fade, 7000);
    }

    /// <summary>
    /// Construye el menú contextual con el aspecto de la app. La plantilla
    /// NATIVA de los items resalta con los pinceles claros del tema del
    /// sistema, así que se sustituye por la nuestra, que vive en el
    /// diccionario de estilos compartido. OJO: el estilo va PUESTO EN CADA
    /// ITEM y no en <c>ItemContainerStyle</c>, porque ese aplica a todos los
    /// contenedores del menú — y un estilo de MenuItem sobre un Separator
    /// hace que WPF lance una excepción.
    /// </summary>
    /// <returns>El menú listo para entregar al icono.</returns>
    private ContextMenu CrearMenu()
    {
        var recursos = new ResourceDictionary
        {
            Source = new Uri("Recursos/EstilosDiscord.xaml", UriKind.Relative),
        };
        var estiloItem = (Style)recursos["ItemMenuBandeja"];

        var menu = new ContextMenu
        {
            // Plantilla COMPLETA propia: sin ella, la nativa (con su
            // ScrollViewer y su marco del sistema) deja recortes blancos en
            // Windows 11 al teñir el menú de oscuro
            Style = (Style)recursos["MenuBandeja"],
            Background = PincelFondo,
            Foreground = PincelTexto,
            BorderBrush = PincelBorde,
            BorderThickness = new Thickness(1),
            FontSize = 13,
            MinWidth = 200,
        };

        var abrir = new MenuItem { Header = "Abrir AulaChatNet", Style = estiloItem };
        abrir.Click += (_, _) => AbrirPulsado?.Invoke(this, EventArgs.Empty);

        var salir = new MenuItem { Header = "Salir de AulaChatNet", Style = estiloItem };
        salir.Click += (_, _) => SalirPulsado?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(abrir);
        menu.Items.Add(new Separator());
        menu.Items.Add(salir);

        return menu;
    }

    /// <summary>
    /// Crea un pincel opaco de la paleta Discord y lo congela.
    /// </summary>
    /// <param name="r">Componente roja (0-255).</param>
    /// <param name="g">Componente verde (0-255).</param>
    /// <param name="b">Componente azul (0-255).</param>
    /// <returns>Pincel congelado listo para compartir.</returns>
    private static SolidColorBrush CrearPincel(byte r, byte g, byte b)
    {
        var pincel = new SolidColorBrush(Color.FromRgb(r, g, b));
        pincel.Freeze();
        return pincel;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Sin Dispose, el icono queda HUÉRFANO en la bandeja (pantallita
        // muerta) hasta el siguiente logoff: el icono se retira siempre
        if (_icono is null)
        {
            return;
        }

        _icono.Dispose();
        _icono = null;
        Log.Information("Icono de bandeja retirado");
    }
}
