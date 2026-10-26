using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Brillo y contraste lineales: contraste × imagen + brillo, saturado al rango del tipo. El contraste
/// va en centésimas; el brillo, en unidades del tipo (el worker exige ±255 en 8 bits).
/// </summary>
public sealed class BrilloContraste : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.tonal.brillo-contraste/v1";

    private BrilloContraste(int contrasteCentesimas, int brillo)
    {
        (ContrasteCentesimas, Brillo) = (contrasteCentesimas, brillo);
        ParametrosCanonicos = Canonico(("contrasteCentesimas", contrasteCentesimas), ("brillo", brillo));
    }

    public int ContrasteCentesimas { get; }
    public int Brillo { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static BrilloContraste Desde(LectorDeParametros p)
    {
        var (contraste, brillo) = (p.Entero("contrasteCentesimas"), p.Entero("brillo"));
        if (contraste is < 1 or > 1000)
            throw new ExcepcionDeDominio("contrasteCentesimas va de 1 (0,01) a 1000 (10).");
        if (brillo is < -65_535 or > 65_535)
            throw new ExcepcionDeDominio("El brillo va de -65535 a 65535.");
        return new BrilloContraste(contraste, brillo);
    }
}

/// <summary>Corrección gamma: salida = entrada^(1/γ), normalizada al rango del tipo. γ &gt; 1 aclara. En milésimas.</summary>
public sealed class Gamma : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.tonal.gamma/v1";

    private Gamma(int gammaMilesimas)
    {
        GammaMilesimas = gammaMilesimas;
        ParametrosCanonicos = Canonico(("gammaMilesimas", gammaMilesimas));
    }

    public int GammaMilesimas { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static Gamma Desde(LectorDeParametros p)
    {
        var gamma = p.Entero("gammaMilesimas");
        if (gamma is < 100 or > 10_000)
            throw new ExcepcionDeDominio("gammaMilesimas va de 100 (0,1) a 10000 (10).");
        return new Gamma(gamma);
    }
}

/// <summary>Ecualización del histograma; en color, sobre la luminancia (Y de YCrCb). Sólo 8 bits. Sin parámetros.</summary>
public sealed class Ecualizacion : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.tonal.ecualizacion/v1";

    private Ecualizacion() => ParametrosCanonicos = Canonico();

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static Ecualizacion Desde(LectorDeParametros p) => new();
}
