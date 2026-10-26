using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Reducción de ruido por medias no locales (NL-means). <see cref="H"/> es la fuerza sobre la
/// luminancia y <see cref="HColor"/> sobre el color (se ignora en imágenes en gris). Sólo 8 bits:
/// lo valida el worker.
/// </summary>
public sealed class DenoiseNlMeans : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.denoise.nlmeans/v1";

    private DenoiseNlMeans(int h, int hColor, int plantilla, int busqueda)
    {
        (H, HColor, Plantilla, Busqueda) = (h, hColor, plantilla, busqueda);
        ParametrosCanonicos = Canonico(("h", h), ("hColor", hColor), ("plantilla", plantilla), ("busqueda", busqueda));
    }

    public int H { get; }
    public int HColor { get; }
    public int Plantilla { get; }
    public int Busqueda { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static DenoiseNlMeans Desde(LectorDeParametros p)
    {
        var (h, hColor) = (p.Entero("h"), p.Entero("hColor"));
        if (h is < 1 or > 100 || hColor is < 0 or > 100)
            throw new ExcepcionDeDominio("La fuerza h va de 1 a 100 y hColor de 0 a 100.");
        var plantilla = Ventanas.Impar(p, "plantilla", 3, 21);
        var busqueda = Ventanas.Impar(p, "busqueda", 3, 51);
        if (busqueda < plantilla)
            throw new ExcepcionDeDominio("La ventana de búsqueda no puede ser menor que la plantilla.");
        return new DenoiseNlMeans(h, hColor, plantilla, busqueda);
    }
}

/// <summary>Filtro de mediana. En 16 bits OpenCV sólo admite ventanas de 3 o 5: lo valida el worker.</summary>
public sealed class DenoiseMediana : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.denoise.mediana/v1";

    private DenoiseMediana(int ventana)
    {
        Ventana = ventana;
        ParametrosCanonicos = Canonico(("ventana", ventana));
    }

    public int Ventana { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static DenoiseMediana Desde(LectorDeParametros p) => new(Ventanas.Impar(p, "ventana", 3, 15));
}

internal static class Ventanas
{
    public static int Impar(LectorDeParametros p, string nombre, int minimo, int maximo)
    {
        var valor = p.Entero(nombre);
        if (valor < minimo || valor > maximo || valor % 2 == 0)
            throw new ExcepcionDeDominio($"La ventana '{nombre}' debe ser impar, entre {minimo} y {maximo}.");
        return valor;
    }
}
