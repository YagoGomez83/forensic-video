using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Controles;

/// <summary>
/// Dibuja la evidencia con la <see cref="TransformacionDeVista"/> del visor y le pasa al ViewModel el
/// zoom (rueda, en el cursor) y el desplazamiento (botón derecho o central). No calcula coordenadas
/// de imagen: eso es de la transformación.
/// </summary>
public class LienzoDeEvidencia : Control
{
    public static readonly StyledProperty<Bitmap?> ImagenProperty =
        AvaloniaProperty.Register<LienzoDeEvidencia, Bitmap?>(nameof(Imagen));

    public static readonly StyledProperty<TransformacionDeVista?> TransformacionProperty =
        AvaloniaProperty.Register<LienzoDeEvidencia, TransformacionDeVista?>(nameof(Transformacion));

    public const double FactorDeZoom = 1.25;

    private Point? _ultimoArrastre;

    static LienzoDeEvidencia()
    {
        AffectsRender<LienzoDeEvidencia>(ImagenProperty, TransformacionProperty);
        ClipToBoundsProperty.OverrideDefaultValue<LienzoDeEvidencia>(true);
        FocusableProperty.OverrideDefaultValue<LienzoDeEvidencia>(true);
    }

    public Bitmap? Imagen
    {
        get => GetValue(ImagenProperty);
        set => SetValue(ImagenProperty, value);
    }

    public TransformacionDeVista? Transformacion
    {
        get => GetValue(TransformacionProperty);
        set => SetValue(TransformacionProperty, value);
    }

    protected VisorViewModel? Visor => DataContext as VisorViewModel;

    public override void Render(DrawingContext contexto)
    {
        // Fondo transparente para recibir el puntero en toda el área.
        contexto.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Imagen is not { } imagen || Transformacion is not { } t)
            return;
        var tamano = imagen.PixelSize;
        var destino = new Rect(t.Desplazamiento.X, t.Desplazamiento.Y, tamano.Width * t.Zoom, tamano.Height * t.Zoom);
        // Con zoom ≥ 1, vecino más cercano: se ven los píxeles tal cual son, sin suavizado inventado.
        var modo = t.Zoom >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (contexto.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = modo }))
            contexto.DrawImage(imagen, new Rect(0, 0, tamano.Width, tamano.Height), destino);
        DibujarEncima(contexto, t);
    }

    /// <summary>Para lo que se dibuja sobre la imagen (p. ej. la región que se está marcando).</summary>
    protected virtual void DibujarEncima(DrawingContext contexto, TransformacionDeVista t)
    {
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        Visor?.ZoomEn(e.Delta.Y > 0 ? FactorDeZoom : 1 / FactorDeZoom, new Punto(p.X, p.Y));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var punto = e.GetCurrentPoint(this);
        if (punto.Properties.IsRightButtonPressed || punto.Properties.IsMiddleButtonPressed)
        {
            _ultimoArrastre = punto.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_ultimoArrastre is not { } anterior)
            return;
        var actual = e.GetPosition(this);
        Visor?.Desplazar(actual.X - anterior.X, actual.Y - anterior.Y);
        _ultimoArrastre = actual;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_ultimoArrastre is null || e.InitialPressMouseButton is not (MouseButton.Right or MouseButton.Middle))
            return;
        _ultimoArrastre = null;
        e.Pointer.Capture(null);
    }
}
