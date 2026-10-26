using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Máscara de enfoque (unsharp): imagen + cantidad × (imagen − desenfoque gaussiano). Sigma y cantidad
/// en centésimas: enteros, para que los parámetros canónicos no dependan de cómo se escribe un float.
/// </summary>
public sealed class RealceUnsharp : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.realce.unsharp/v1";

    private RealceUnsharp(int sigmaCentesimas, int cantidadCentesimas)
    {
        (SigmaCentesimas, CantidadCentesimas) = (sigmaCentesimas, cantidadCentesimas);
        ParametrosCanonicos = Canonico(("sigmaCentesimas", sigmaCentesimas), ("cantidadCentesimas", cantidadCentesimas));
    }

    public int SigmaCentesimas { get; }
    public int CantidadCentesimas { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static RealceUnsharp Desde(LectorDeParametros p)
    {
        var (sigma, cantidad) = (p.Entero("sigmaCentesimas"), p.Entero("cantidadCentesimas"));
        if (sigma is < 10 or > 5000)
            throw new ExcepcionDeDominio("sigmaCentesimas va de 10 (0,1) a 5000 (50).");
        if (cantidad is < 1 or > 1000)
            throw new ExcepcionDeDominio("cantidadCentesimas va de 1 (0,01) a 1000 (10).");
        return new RealceUnsharp(sigma, cantidad);
    }
}

/// <summary>
/// Ecualización adaptativa del histograma con límite de contraste (CLAHE). En color se aplica sobre
/// la luminancia (L de Lab). Límite en décimas. Admite gris de 8 o 16 bits y BGR de 8: lo valida el worker.
/// </summary>
public sealed class RealceClahe : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.realce.clahe/v1";

    private RealceClahe(int limiteDecimas, int cuadricula)
    {
        (LimiteDecimas, Cuadricula) = (limiteDecimas, cuadricula);
        ParametrosCanonicos = Canonico(("limiteDecimas", limiteDecimas), ("cuadricula", cuadricula));
    }

    public int LimiteDecimas { get; }
    public int Cuadricula { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static RealceClahe Desde(LectorDeParametros p)
    {
        var (limite, cuadricula) = (p.Entero("limiteDecimas"), p.Entero("cuadricula"));
        if (limite is < 1 or > 400)
            throw new ExcepcionDeDominio("limiteDecimas va de 1 (0,1) a 400 (40).");
        if (cuadricula is < 1 or > 64)
            throw new ExcepcionDeDominio("La cuadrícula va de 1 a 64 celdas por lado.");
        return new RealceClahe(limite, cuadricula);
    }
}
