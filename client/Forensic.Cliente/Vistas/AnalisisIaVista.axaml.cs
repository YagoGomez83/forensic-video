using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Forensic.Cliente.Nucleo.Ia;

namespace Forensic.Cliente.Vistas;

/// <summary>
/// Decodifica el contenido que ya tiene el visor (sólo para mostrar) y dibuja encima los hallazgos.
/// Igual que el visor, no aplica ninguna transformación a la imagen: la grilla es la del worker.
/// </summary>
public partial class AnalisisIaVista : UserControl
{
    private AnalisisIaViewModel? _vm;
    private bool _ajustada;

    public AnalisisIaVista()
    {
        InitializeComponent();
        Lienzo.SizeChanged += (_, _) => AjustarSiHaceFalta();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as AnalisisIaViewModel;
        Mostrar();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _vm?.CargarCommand.Execute(null);
    }

    private void Mostrar()
    {
        Lienzo.Imagen?.Dispose();
        Lienzo.Imagen = null;
        _ajustada = false;
        if (_vm?.Visor is not { Contenido: { } contenido } visor)
            return;
        try
        {
            Lienzo.Imagen = new Bitmap(new MemoryStream(contenido, writable: false));
            visor.ImagenDecodificada(Lienzo.Imagen.PixelSize.Width, Lienzo.Imagen.PixelSize.Height);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            visor.NoDecodificable();
        }
        AjustarSiHaceFalta();
    }

    private void AjustarSiHaceFalta()
    {
        if (_ajustada || _vm?.Visor is not { AnchoImagen: > 0 } visor || Lienzo.Bounds.Width <= 0)
            return;
        visor.AjustarA(Lienzo.Bounds.Width, Lienzo.Bounds.Height);
        _ajustada = true;
    }
}
