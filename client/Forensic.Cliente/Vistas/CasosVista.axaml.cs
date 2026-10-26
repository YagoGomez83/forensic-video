using Avalonia.Controls;
using Avalonia.Interactivity;
using Forensic.Cliente.Nucleo.Casos;

namespace Forensic.Cliente.Vistas;

public partial class CasosVista : UserControl
{
    public CasosVista() => InitializeComponent();

    /// <summary>Al mostrarse, pide la carga. Toda la lógica está en el ViewModel.</summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as CasosViewModel)?.CargarCommand.Execute(null);
    }
}
