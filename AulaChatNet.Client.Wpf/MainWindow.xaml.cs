using AulaChatNet.Client.Web.Configuracion;
using Serilog;
using System.Windows;                    // Window, Thickness
using System.Windows.Controls;           // TextBlock
using System.Windows.Media;              // Brushes
using Microsoft.Web.WebView2.Core;       // CoreWebView2Environment (motor embebido)

namespace AulaChatNet.Client.Wpf;

/// <summary>
/// Ventana principal: un único <c>BlazorWebView</c> a sangre que monta la
/// UI del chat (la misma de la web, sin duplicar ni una línea).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Construye la ventana, entrega el proveedor de servicios al
    /// <c>BlazorWebView</c> y comprueba que el motor embebido (WebView2)
    /// exista en la máquina.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // App.OnStartup construye los servicios ANTES de crear esta ventana,
        // así que aquí ya están listos. El BlazorWebView los usa como raíz
        // para resolver los @inject de los componentes
        if (App.Servicios is null)
        {
            throw new InvalidOperationException(
                "Los servicios no están construidos: la ventana debe crearse desde App.OnStartup.");
        }

        Navegador.Services = App.Servicios;

        // WebView2 es el motor Chromium embebido de Windows que pinta el DOM.
        // Si la máquina no lo tiene, la ventana se levantaría VACÍA sin más:
        // se comprueba y se avisa en el propio marco
        AvisarSiFaltaWebView2();
    }

    /// <summary>
    /// Si WebView2 no está disponible en esta máquina, sustituye el navegador
    /// por un mensaje con el enlace de descarga.
    /// </summary>
    private void AvisarSiFaltaWebView2()
    {
        try
        {
            // Chequeo REAL del motor: devuelve la versión instalada y si no
            // hay WebView2 lanza WebView2RuntimeNotFoundException
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            Log.Information("WebView2 disponible (versión {Version})", version);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Log.Warning("WebView2 no está instalado en esta máquina");

            Content = new TextBlock
            {
                Text = "Esta aplicación necesita el motor WebView2 de Microsoft.\n\n" +
                       "Descárgalo desde https://developer.microsoft.com/microsoft-edge/webview2/ " +
                       "y vuelve a abrir AulaChatNet.",
                Foreground = Brushes.White,
                FontSize = 16,
                Margin = new Thickness(40),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        catch (Exception ex)
        {
            // Un fallo en la comprobación no debe tumbar la app sin registro
            Log.Fatal(ex, "Error al comprobar la disponibilidad de WebView2");
        }
    }
}
