using Forensic.Domain.Operaciones;

namespace Forensic.Domain.Ia;

/// <summary>
/// Super-resolución x4 (Real-ESRGAN). Sin parámetros: la escala la fija el modelo. El derivado inventa
/// detalle plausible, no recupera el real: es «sólo investigación».
/// </summary>
public sealed class SuperResolucionX4 : OperacionDeIa
{
    public const string TipoVersionado = "ia.superresolucion/v1";

    private SuperResolucionX4()
    {
    }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos => "{}";

    internal static SuperResolucionX4 Desde(LectorDeParametros _) => new();
}
