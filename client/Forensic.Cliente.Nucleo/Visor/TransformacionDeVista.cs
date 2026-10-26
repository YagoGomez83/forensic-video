namespace Forensic.Cliente.Nucleo.Visor;

public readonly record struct Punto(double X, double Y);

/// <summary>Una región en píxeles enteros de la imagen original: lo que viaja al servidor.</summary>
public readonly record struct Region(int X, int Y, int Ancho, int Alto);

/// <summary>
/// Traducción entre la pantalla (con zoom y desplazamiento) y la imagen original. Inmutable. Convención:
/// el píxel i de la imagen ocupa [i, i+1); en pantalla, imagen·zoom + desplazamiento. Lo que se le pide al
/// servidor siempre son píxeles de la imagen, nunca de la pantalla.
/// </summary>
public sealed record TransformacionDeVista(double Zoom, Punto Desplazamiento)
{
    public const double ZoomMinimo = 0.01;
    public const double ZoomMaximo = 64;

    public TransformacionDeVista()
        : this(1, new Punto(0, 0))
    {
    }

    public Punto PantallaAImagen(Punto p) => new((p.X - Desplazamiento.X) / Zoom, (p.Y - Desplazamiento.Y) / Zoom);

    public Punto ImagenAPantalla(Punto p) => new(p.X * Zoom + Desplazamiento.X, p.Y * Zoom + Desplazamiento.Y);

    /// <summary>Multiplica el zoom dejando quieto el punto de la imagen que está bajo <paramref name="cursor"/>.</summary>
    public TransformacionDeVista ZoomEn(double factor, Punto cursor)
    {
        var zoom = Math.Clamp(Zoom * factor, ZoomMinimo, ZoomMaximo);
        var bajoElCursor = PantallaAImagen(cursor);
        return new TransformacionDeVista(zoom, new Punto(cursor.X - bajoElCursor.X * zoom, cursor.Y - bajoElCursor.Y * zoom));
    }

    public TransformacionDeVista Desplazada(double dx, double dy) =>
        this with { Desplazamiento = new Punto(Desplazamiento.X + dx, Desplazamiento.Y + dy) };

    /// <summary>La imagen completa, lo más grande posible y centrada en la vista.</summary>
    public static TransformacionDeVista Ajustada(int anchoImagen, int altoImagen, double anchoVista, double altoVista)
    {
        if (anchoImagen <= 0 || altoImagen <= 0 || anchoVista <= 0 || altoVista <= 0)
            return new TransformacionDeVista();
        var zoom = Math.Clamp(Math.Min(anchoVista / anchoImagen, altoVista / altoImagen), ZoomMinimo, ZoomMaximo);
        return new TransformacionDeVista(zoom, new Punto((anchoVista - anchoImagen * zoom) / 2, (altoVista - altoImagen * zoom) / 2));
    }

    /// <summary>
    /// La región de la imagen que cubre un arrastre de <paramref name="a"/> a <paramref name="b"/> en
    /// pantalla, en cualquier sentido. Incluye todo píxel tocado (origen hacia abajo, extremo hacia
    /// arriba) y se recorta a los bordes de la imagen. Null si no toca la imagen.
    /// </summary>
    public Region? RegionEnImagen(Punto a, Punto b, int anchoImagen, int altoImagen)
    {
        var (ia, ib) = (PantallaAImagen(a), PantallaAImagen(b));
        var (x0, x1) = Intervalo(Math.Min(ia.X, ib.X), Math.Max(ia.X, ib.X), anchoImagen);
        var (y0, y1) = Intervalo(Math.Min(ia.Y, ib.Y), Math.Max(ia.Y, ib.Y), altoImagen);
        return x1 > x0 && y1 > y0 ? new Region(x0, y0, x1 - x0, y1 - y0) : null;
    }

    /// <summary>El píxel de la imagen que contiene el punto de pantalla, acotado a la imagen.</summary>
    public (int X, int Y) PixelEnImagen(Punto p, int anchoImagen, int altoImagen)
    {
        var i = PantallaAImagen(p);
        return (Math.Clamp((int)Math.Floor(i.X), 0, anchoImagen - 1), Math.Clamp((int)Math.Floor(i.Y), 0, altoImagen - 1));
    }

    private static (int Desde, int Hasta) Intervalo(double minimo, double maximo, int lado)
    {
        var desde = (int)Math.Floor(minimo);
        // Un clic sin arrastre (o justo sobre un borde) marca al menos el píxel que contiene al punto.
        var hasta = Math.Max((int)Math.Ceiling(maximo), desde + 1);
        if (desde >= lado || hasta <= 0)
            return (0, 0);
        return (Math.Max(desde, 0), Math.Min(hasta, lado));
    }
}
