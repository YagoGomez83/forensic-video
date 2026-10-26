using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Vistas;

/// <summary>
/// Decodifica el contenido que trajo el ViewModel (sólo para mostrar) y le informa las dimensiones.
/// No aplica ninguna transformación a la imagen: la grilla de píxeles es la misma que ve el worker.
/// </summary>
public partial class VisorVista : UserControl
{
    private VisorViewModel? _vm;
    private bool _ajustada;

    public VisorVista()
    {
        InitializeComponent();
        Lienzo.SizeChanged += (_, _) => AjustarSiHaceFalta();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= AlCambiar;
        _vm = DataContext as VisorViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += AlCambiar;
        Mostrar();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_vm is { Contenido: null })
            _vm.CargarCommand.Execute(null);
    }

    private void AlCambiar(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisorViewModel.Contenido))
            Mostrar();
    }

    private void Mostrar()
    {
        Lienzo.Imagen?.Dispose();
        Lienzo.Imagen = null;
        _ajustada = false;
        if (_vm?.Contenido is not { } contenido)
            return;
        try
        {
            Lienzo.Imagen = new Bitmap(new MemoryStream(contenido, writable: false));
            _vm.ImagenDecodificada(Lienzo.Imagen.PixelSize.Width, Lienzo.Imagen.PixelSize.Height);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            _vm.NoDecodificable();
        }
        AjustarSiHaceFalta();
    }

    /// <summary>El diálogo de guardado es del sistema operativo; la descarga la hace el ViewModel.</summary>
    private async void ElegirDestino(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } almacen)
            return;
        var archivo = await almacen.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar la evidencia",
            SuggestedFileName = vm.Evidencia.NombreOriginal,
        });
        if (archivo is null)
            return;
        await vm.DescargarCommand.ExecuteAsync(new DestinoDeDescarga(archivo.Name, archivo.OpenWriteAsync));
    }

    private void AjustarSiHaceFalta()
    {
        if (_ajustada || _vm is not { AnchoImagen: > 0 } vm || Lienzo.Bounds.Width <= 0)
            return;
        vm.AjustarA(Lienzo.Bounds.Width, Lienzo.Bounds.Height);
        _ajustada = true;
    }
}
