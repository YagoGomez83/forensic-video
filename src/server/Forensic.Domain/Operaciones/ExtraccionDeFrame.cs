using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Extrae el frame <see cref="Indice"/> (desde 0) de un video como imagen PNG. El worker decodifica
/// en orden desde el principio, sin seek: el seek de los demuxers no garantiza caer en el mismo frame.
/// Que el índice exista lo valida el worker, que conoce el video.
/// </summary>
public sealed class ExtraccionDeFrame : OperacionDeterminista
{
    public const string TipoVersionado = "opencv.frame/v1";
    public const int IndiceMaximo = 10_000_000;

    private ExtraccionDeFrame(int indice)
    {
        Indice = indice;
        ParametrosCanonicos = Canonico(("indice", indice));
    }

    public int Indice { get; }

    public override string Tipo => TipoVersionado;
    public override string ParametrosCanonicos { get; }

    internal static ExtraccionDeFrame Desde(LectorDeParametros p)
    {
        var indice = p.Entero("indice");
        if (indice is < 0 or > IndiceMaximo)
            throw new ExcepcionDeDominio($"El índice del frame va de 0 a {IndiceMaximo}.");
        return new ExtraccionDeFrame(indice);
    }
}
