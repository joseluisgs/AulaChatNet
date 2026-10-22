using AulaChatNet.Client.Blazor.Components.Configuracion;
using AulaChatNet.Client.Wpf.Services;         // IBandejaService, PreferenciasBandeja
using Microsoft.Extensions.DependencyInjection; // GetRequiredService
using Serilog;
using System.ComponentModel;                   // CancelEventArgs (Closing)
using System.Windows;                          // Window, RoutedEventArgs
using System.Windows.Controls;                 // TextBlock (aviso de WebView2)
using System.Windows.Media;                    // Brushes
using Microsoft.Web.WebView2.Core;             // CoreWebView2Environment (motor embebido)

namespace AulaChatNet.Client.Wpf;

/// <summary>
/// Ventana principal: un único <c>BlazorWebView</c> a sangre que monta la
/// UI del chat (la misma de la web, sin duplicar ni una línea). La X no
/// cierra la app: la ventana se oculta y la aplicación se mantiene viva en
/// la bandeja del sistema; salir se hace SOLO desde el menú de la bandeja.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Icono de la bandeja (la app en segundo plano).</summary>
    private readonly IBandejaService _bandeja;

    /// <summary>Ajustes de la bandeja (¿ya se enseñó el aviso de segundo plano?).</summary>
    private readonly PreferenciasBandeja _preferencias;

    /// <summary>
    /// Se pone a <c>true</c> cuando el usuario elige "Salir" en el menú de
    /// la bandeja: es la señal para dejar pasar el cierre en vez de
    /// cancelarlo y mandar la ventana al fondo otra vez.
    /// </summary>
    private bool _salidaConfirmada;

    /// <summary>
    /// Construye la ventana, entrega el proveedor de servicios al
    /// <c>BlazorWebView</c>, engancha la bandeja y comprueba que el motor
    /// embebido (WebView2) exista en la máquina.
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
        _bandeja = App.Servicios.GetRequiredService<IBandejaService>();
        _preferencias = App.Servicios.GetRequiredService<PreferenciasBandeja>();

        // Los eventos de la bandeja los resuelve la ventana: es quien sabe
        // ocultarse, restaurarse y salir de verdad cuando toca
        _bandeja.AbrirPulsado += (_, _) => RestaurarVentana();
        _bandeja.SalirPulsado += (_, _) => AlSalirDeLaBandeja();

        // WebView2 es el motor Chromium embebido de Windows que pinta el DOM.
        // Si la máquina no lo tiene, la ventana se levantaría VACÍA sin más:
        // se comprueba y se avisa en el propio marco
        AvisarSiFaltaWebView2();
    }

    #region Barra de título

    /// <summary>
    /// Minimiza la ventana al escritorio (la app sigue en su sitio, en la
    /// barra de tareas, igual que cualquier otra).
    /// </summary>
    /// <param name="sender">Botón pulsado (no se usa).</param>
    /// <param name="e">Argumentos del clic (no se usan).</param>
    private void BotonMinimizar_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    /// <summary>
    /// La X NO mata la app: llama a <c>Close()</c> y el manejador
    /// <c>AlCerrarVentana</c> intercepta el cierre y la manda a la bandeja.
    /// </summary>
    /// <param name="sender">Botón pulsado (no se usa).</param>
    /// <param name="e">Argumentos del clic (no se usan).</param>
    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();

    #endregion

    #region Bandeja del sistema

    /// <summary>
    /// Intercepta el cierre de la ventana: si el usuario no ha confirmado la
    /// salida desde la bandeja, se CANCELA y la app pasa a segundo plano
    /// (viva y conectada al aula). Es el comportamiento de Discord o Slack.
    /// </summary>
    /// <param name="sender">La ventana (no se usa).</param>
    /// <param name="e">Argumentos del cierre; se cancela salvo confirmación.</param>
    private async void AlCerrarVentana(object? sender, CancelEventArgs e)
    {
        // Cierre REAL (Salir del menú de la bandeja): dejamos pasar
        if (_salidaConfirmada)
        {
            return;
        }

        // Cualquier otro intento (la X, Alt+F4) se cancela: la ventana se
        // oculta y la app se queda en la bandeja, con la conexión intacta
        e.Cancel = true;
        Hide();

        // La bandeja es COMODIDAD: si algo falla aquí (icono, burbuja…), se
        // registra y se sigue — jamás se tumba el chat por un icono
        try
        {
            _bandeja.Mostrar();

            // El aviso "sigue en segundo plano" se enseña UNA sola vez en la
            // vida del usuario (queda guardado en el fichero de preferencias)
            if (await _preferencias.EsPrimeraVezAsync())
            {
                _bandeja.AvisarSegundoPlano();
                await _preferencias.MarcarAvisoMostradoAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error con el icono de bandeja: la app sigue viva en segundo plano sin él");
        }
    }

    /// <summary>
    /// Vuelve a mostrar la ventana desde la bandeja (clic, doble clic o
    /// "Abrir" del menú), restaurada y en primer plano.
    /// </summary>
    private void RestaurarVentana()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Sale de la aplicación de verdad (menú de la bandeja → "Salir"). Sin
    /// confirmación, como cualquier app de escritorio: abrir el menú y
    /// elegir "Salir" YA es el paso deliberado. Se marca la salida y se
    /// ordena el apagado — al cerrarse la ventana, <c>AlCerrarVentana</c>
    /// dejará pasar el cierre en vez de mandarla a la bandeja otra vez.
    /// </summary>
    private void AlSalirDeLaBandeja()
    {
        Log.Information("Salida desde el menú de la bandeja");
        _salidaConfirmada = true;
        Application.Current.Shutdown();
    }

    #endregion

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
