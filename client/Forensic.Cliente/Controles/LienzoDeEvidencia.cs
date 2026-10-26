using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Globalization;
using Forensic.Cliente.Nucleo.Operaciones;
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

    /// <summary>La región marcada, en píxeles de la imagen: se dibuja tal cual se va a enviar.</summary>
    public static readonly StyledProperty<Region?> RegionProperty =
        AvaloniaProperty.Register<LienzoDeEvidencia, Region?>(nameof(Region));

    /// <summary>Los puntos marcados, en píxeles de la imagen; se dibujan numerados en su centro.</summary>
    public static readonly StyledProperty<IReadOnlyList<PuntoDeImagen>?> PuntosProperty =
        AvaloniaProperty.Register<LienzoDeEvidencia, IReadOnlyList<PuntoDeImagen>?>(nameof(Puntos));

    public const double FactorDeZoom = 1.25;

    private static readonly IPen TrazoDeRegion = new Pen(Brushes.Yellow, 1.5, dashStyle: DashStyle.Dash);

    private Point? _ultimoArrastre;
    private Point? _inicioDeRegion;

    static LienzoDeEvidencia()
    {
        AffectsRender<LienzoDeEvidencia>(ImagenProperty, TransformacionProperty, RegionProperty, PuntosProperty);
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

    public Region? Region
    {
        get => GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    public IReadOnlyList<PuntoDeImagen>? Puntos
    {
        get => GetValue(PuntosProperty);
        set => SetValue(PuntosProperty, value);
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
        if (Region is { } r)
        {
            var a = t.ImagenAPantalla(new Punto(r.X, r.Y));
            var b = t.ImagenAPantalla(new Punto(r.X + r.Ancho, r.Y + r.Alto));
            contexto.DrawRectangle(null, TrazoDeRegion, new Rect(new Point(a.X, a.Y), new Point(b.X, b.Y)));
        }
        if (Puntos is not { Count: > 0 } puntos)
            return;
        for (var i = 0; i < puntos.Count; i++)
        {
            var c = t.ImagenAPantalla(new Punto(puntos[i].X + 0.5, puntos[i].Y + 0.5));
            contexto.DrawEllipse(Brushes.Yellow, null, new Point(c.X, c.Y), 4, 4);
            var numero = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.Yellow);
            contexto.DrawText(numero, new Point(c.X + 6, c.Y - 18));
        }
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
        else if (punto.Properties.IsLeftButtonPressed && Visor?.Modo == ModoDeMarcado.Puntos)
        {
            Visor.AgregarPunto(new Punto(punto.Position.X, punto.Position.Y));
            e.Handled = true;
        }
        else if (punto.Properties.IsLeftButtonPressed)
        {
            _inicioDeRegion = punto.Position;
            MarcarHasta(punto.Position);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void MarcarHasta(Point actual)
    {
        if (_inicioDeRegion is { } inicio)
            Visor?.MarcarRegion(new Punto(inicio.X, inicio.Y), new Punto(actual.X, actual.Y));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_inicioDeRegion is not null)
        {
            MarcarHasta(e.GetPosition(this));
            return;
        }
        if (_ultimoArrastre is not { } anterior)
            return;
        var actual = e.GetPosition(this);
        Visor?.Desplazar(actual.X - anterior.X, actual.Y - anterior.Y);
        _ultimoArrastre = actual;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_inicioDeRegion is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            MarcarHasta(e.GetPosition(this));
            _inicioDeRegion = null;
            e.Pointer.Capture(null);
            return;
        }
        if (_ultimoArrastre is null || e.InitialPressMouseButton is not (MouseButton.Right or MouseButton.Middle))
            return;
        _ultimoArrastre = null;
        e.Pointer.Capture(null);
    }
}
