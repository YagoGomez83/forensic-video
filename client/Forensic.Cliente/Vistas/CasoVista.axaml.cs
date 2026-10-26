using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Forensic.Cliente.Nucleo.Casos;

namespace Forensic.Cliente.Vistas;

public partial class CasoVista : UserControl
{
    public CasoVista() => InitializeComponent();

    private CasoViewModel? Vm => DataContext as CasoViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Vm?.CargarCommand.Execute(null);
    }

    /// <summary>
    /// El diálogo de archivos es del sistema operativo, así que vive en la vista. El archivo se lee
    /// directo desde donde está, sin copias temporales.
    /// </summary>
    private async void ElegirArchivo(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } almacen)
            return;
        var elegidos = await almacen.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Elegir la evidencia", AllowMultiple = false });
        if (elegidos is not [var archivo])
            return;
        var propiedades = await archivo.GetBasicPropertiesAsync();
        await vm.IngestarCommand.ExecuteAsync(
            new ArchivoParaIngestar(archivo.Name, (long)(propiedades.Size ?? 0), archivo.OpenReadAsync));
    }
}
