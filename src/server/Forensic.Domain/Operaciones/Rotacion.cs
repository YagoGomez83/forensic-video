using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>Rotación exacta en cuartos de vuelta, sentido horario: sin interpolación.</summary>
public sealed class Rotacion90 : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.rotacion90/v1";

    private Rotacion90(int giros)
    {
        Giros = giros;
        ParametrosCanonicos = Canonico(("giros", giros));
    }

    /// <summary>1 = 90°, 2 = 180°, 3 = 270°, en sentido horario.</summary>
    public int Giros { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static Rotacion90 Desde(LectorDeParametros p)
    {
        var giros = p.Entero("giros");
        if (giros is < 1 or > 3)
            throw new ExcepcionDeDominio("Los giros de una rotación exacta son 1, 2 o 3 cuartos de vuelta.");
        return new Rotacion90(giros);
    }
}

/// <summary>
/// Rotación por un ángulo arbitrario alrededor del centro, conservando el tamaño (las esquinas que
/// salen se pierden, lo que entra es negro). Positivo = antihorario, como en OpenCV. El ángulo va en
/// centésimas de grado: un entero, para que los parámetros canónicos no dependan de cómo se escribe un float.
/// </summary>
public sealed class Rotacion : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.rotacion/v1";
    public const int CentesimasMaximas = 36_000;

    private Rotacion(int centesimasDeGrado, string interpolacion)
    {
        CentesimasDeGrado = centesimasDeGrado;
        Interpolacion = interpolacion;
        ParametrosCanonicos = Canonico(("centesimasDeGrado", centesimasDeGrado), ("interpolacion", interpolacion));
    }

    public int CentesimasDeGrado { get; }
    public string Interpolacion { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static Rotacion Desde(LectorDeParametros p)
    {
        var centesimas = p.Entero("centesimasDeGrado");
        if (centesimas == 0 || Math.Abs(centesimas) > CentesimasMaximas)
            throw new ExcepcionDeDominio($"El ángulo debe ser distinto de cero y de hasta ±{CentesimasMaximas} centésimas de grado.");
        return new Rotacion(centesimas, Interpolaciones.Leer(p));
    }
}
