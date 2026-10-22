namespace AulaChatNet.Client.Wpf.Services;

/// <summary>
/// Define el contrato de la bandeja del sistema (la esquina inferior derecha
/// de Windows). El icono es el "ancla" de la app en segundo plano: cuando la
/// ventana se oculta, la aplicación sigue viva y conectada al aula, y este
/// servicio es el ÚNICO camino de vuelta (abrir) y el ÚNICO camino de salida
/// (salir, con confirmación). Implementado con H.NotifyIcon.Wpf.
/// </summary>
public interface IBandejaService : IDisposable
{
    /// <summary>
    /// Crea el icono en la bandeja y lo muestra. Si ya estaba visible, no
    /// hace nada (llamarlo cada vez que la ventana se oculta es inocuo).
    /// </summary>
    void Mostrar();

    /// <summary>
    /// Muestra la burbuja informativa de "la app sigue en segundo plano".
    /// Solo tiene sentido llamarlo la primera vez que se oculta la ventana.
    /// </summary>
    void AvisarSegundoPlano();

    /// <summary>
    /// Se dispara al pulsar "Abrir" en el menú contextual o al hacer doble
    /// clic (o clic izquierdo) en el icono.
    /// </summary>
    event EventHandler? AbrirPulsado;

    /// <summary>
    /// Se dispara al pulsar "Salir" en el menú contextual. Quien reciba el
    /// evento cierra la aplicación (sin confirmación: el menú ya es el paso
    /// deliberado).
    /// </summary>
    event EventHandler? SalirPulsado;
}
