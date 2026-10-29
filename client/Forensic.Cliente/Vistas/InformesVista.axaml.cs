using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Informes;
using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Vistas;

public partial class InformesVista : UserControl
{
    public InformesVista() => InitializeComponent();

    private InformesViewModel? Vm => DataContext as InformesViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Vm?.CargarCommand.Execute(null);
    }

    private void GuardarPdf(object? sender, RoutedEventArgs e) => Guardar(sender, ArchivoDeInforme.Pdf);

    private void GuardarBundle(object? sender, RoutedEventArgs e) => Guardar(sender, ArchivoDeInforme.Bundle);

    /// <summary>El diálogo de guardado es del sistema operativo; la descarga la hace el ViewModel.</summary>
    private async void Guardar(object? sender, ArchivoDeInforme archivo)
    {
        if (Vm is not { } vm || (sender as Control)?.DataContext is not InformeEmitidoDto informe
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } almacen)
            return;
        var destino = await almacen.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = archivo == ArchivoDeInforme.Pdf ? "Guardar el PDF del informe" : "Guardar el bundle verificable",
            SuggestedFileName = vm.NombreSugerido(informe, archivo),
        });
        if (destino is null)
            return;
        await vm.DescargarCommand.ExecuteAsync(new DescargaDeInforme(informe, archivo, new DestinoDeDescarga(destino.Name, destino.OpenWriteAsync)));
    }
}
