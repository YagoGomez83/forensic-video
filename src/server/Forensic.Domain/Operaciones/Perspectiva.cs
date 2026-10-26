using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Rectificación de perspectiva: lleva el cuadrilátero (x1,y1)…(x4,y4) (sup. izq., sup. der., inf.
/// der., inf. izq.) a un rectángulo de <see cref="Ancho"/>×<see cref="Alto"/>. Que los puntos caigan
/// dentro de la imagen y formen un cuadrilátero convexo lo valida el worker.
/// </summary>
public sealed class Perspectiva : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.perspectiva/v1";
    public const long PixelesMaximos = 100_000_000;

    private Perspectiva(int[] puntos, int ancho, int alto, string interpolacion)
    {
        Puntos = puntos;
        Ancho = ancho;
        Alto = alto;
        Interpolacion = interpolacion;
        ParametrosCanonicos = Canonico(
            ("x1", puntos[0]), ("y1", puntos[1]), ("x2", puntos[2]), ("y2", puntos[3]),
            ("x3", puntos[4]), ("y3", puntos[5]), ("x4", puntos[6]), ("y4", puntos[7]),
            ("ancho", ancho), ("alto", alto), ("interpolacion", interpolacion));
    }

    /// <summary>x1, y1, x2, y2, x3, y3, x4, y4.</summary>
    public IReadOnlyList<int> Puntos { get; }
    public int Ancho { get; }
    public int Alto { get; }
    public string Interpolacion { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static Perspectiva Desde(LectorDeParametros p)
    {
        var puntos = new[] { "x1", "y1", "x2", "y2", "x3", "y3", "x4", "y4" }.Select(p.Entero).ToArray();
        if (puntos.Any(c => c is < 0 or > Recorte.LadoMaximo))
            throw new ExcepcionDeDominio($"Los puntos de la perspectiva deben estar entre 0 y {Recorte.LadoMaximo}.");
        var (ancho, alto) = (p.Entero("ancho"), p.Entero("alto"));
        if (ancho < 1 || alto < 1 || (long)ancho * alto > PixelesMaximos)
            throw new ExcepcionDeDominio($"La salida de la perspectiva debe tener entre 1 y {PixelesMaximos} píxeles.");
        return new Perspectiva(puntos, ancho, alto, Interpolaciones.Leer(p));
    }
}

/// <summary>Modos de interpolación admitidos: los tres de OpenCV que son deterministas y conocidos.</summary>
internal static class Interpolaciones
{
    public static string Leer(LectorDeParametros p) => p.Opcion("interpolacion", "vecino", "lineal", "cubica");
}
