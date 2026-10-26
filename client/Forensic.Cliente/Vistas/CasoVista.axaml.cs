using Avalonia.Controls;
using Avalonia.Interactivity;
using Forensic.Cliente.Nucleo.Casos;

namespace Forensic.Cliente.Vistas;

public partial class CasoVista : UserControl
{
    public CasoVista() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as CasoViewModel)?.CargarCommand.Execute(null);
    }
}
