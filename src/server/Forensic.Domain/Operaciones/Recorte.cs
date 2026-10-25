using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Recorte de una región rectangular (ROI). Enteros: trivialmente determinista, sin interpolación.
/// Que la región caiga dentro de la imagen lo valida el worker, que conoce sus dimensiones.
/// </summary>
public sealed class Recorte : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.recorte/v1";
    public const int LadoMaximo = 65_535;

    private Recorte(int x, int y, int ancho, int alto)
    {
        X = x;
        Y = y;
        Ancho = ancho;
        Alto = alto;
        ParametrosCanonicos = Canonico(("x", x), ("y", y), ("ancho", ancho), ("alto", alto));
    }

    public int X { get; }
    public int Y { get; }
    public int Ancho { get; }
    public int Alto { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    public static Recorte Crear(int x, int y, int ancho, int alto)
    {
        if (x < 0 || y < 0 || x > LadoMaximo || y > LadoMaximo)
            throw new ExcepcionDeDominio($"El origen del recorte debe estar entre 0 y {LadoMaximo}.");
        if (ancho < 1 || alto < 1 || ancho > LadoMaximo || alto > LadoMaximo)
            throw new ExcepcionDeDominio($"El ancho y el alto del recorte deben estar entre 1 y {LadoMaximo}.");
        return new Recorte(x, y, ancho, alto);
    }

    internal static Recorte Desde(LectorDeParametros p) =>
        Crear(p.Entero("x"), p.Entero("y"), p.Entero("ancho"), p.Entero("alto"));
}
