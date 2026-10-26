using Avalonia.Controls;
using Avalonia.Interactivity;
using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Vistas;

public partial class LinajeVista : UserControl
{
    public LinajeVista() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as LinajeViewModel)?.CargarCommand.Execute(null);
    }
}
