using Forensic.Domain.Comun;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Ia;

/// <summary>
/// Lo que reporta el modelo, antes de registrarse: región, clase o texto leído, confianza y el dato
/// crudo. Es una pista, no una conclusión.
/// </summary>
public sealed record HallazgoPropuesto
{
    public const int LongitudMaximaClase = 100;
    public const int LongitudMaximaTexto = 200;

    /// <summary>El crudo viene de la workstation: entrada no confiable, con tope.</summary>
    public const int LongitudMaximaCrudo = 4096;

    private HallazgoPropuesto(RegionDeImagen region, string? clase, string? textoLeido, decimal confianza, string datoCrudo)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(datoCrudo);
        if (confianza is < 0m or > 1m)
            throw new ExcepcionDeDominio("La confianza de un hallazgo va de 0 a 1.");
        if (datoCrudo.Length > LongitudMaximaCrudo)
            throw new ExcepcionDeDominio($"El dato crudo de un hallazgo admite hasta {LongitudMaximaCrudo} caracteres.");
        if (!Json.EsObjeto(datoCrudo))
            throw new ExcepcionDeDominio("Todo hallazgo conserva el dato crudo del modelo, como objeto JSON.");
        (Region, Clase, TextoLeido, Confianza, DatoCrudo) = (region, clase, textoLeido, confianza, datoCrudo);
    }

    public RegionDeImagen Region { get; }
    public string? Clase { get; }
    public string? TextoLeido { get; }
    public decimal Confianza { get; }
    public string DatoCrudo { get; }

    /// <summary>Un objeto detectado: «el modelo vio un auto con 0,82».</summary>
    public static HallazgoPropuesto Deteccion(RegionDeImagen region, string clase, decimal confianza, string datoCrudo) =>
        new(region, Validar(clase, LongitudMaximaClase, "clase"), null, confianza, datoCrudo);

    /// <summary>Un texto leído: «el modelo leyó ABC123 con 0,82», nunca «la patente es ABC123».</summary>
    public static HallazgoPropuesto Texto(RegionDeImagen region, string textoLeido, decimal confianza, string datoCrudo) =>
        new(region, null, Validar(textoLeido, LongitudMaximaTexto, "texto leído"), confianza, datoCrudo);

    private static string Validar(string valor, int maximo, string nombre)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length > maximo || valor.Any(char.IsControl))
            throw new ExcepcionDeDominio(
                $"El {nombre} es obligatorio, de hasta {maximo} caracteres y sin caracteres de control.");
        return valor;
    }
}

/// <summary>
/// Un hallazgo registrado de un <see cref="AnalisisIa"/>. Inmutable y siempre «sólo investigación»:
/// la vía no es un dato que se guarde o se asigne, es una constante del tipo.
/// </summary>
public sealed class Hallazgo
{
    private Hallazgo()
    {
        Region = null!;
        DatoCrudo = null!;
    }

    public Guid Id { get; private set; }
    public Guid AnalisisId { get; private set; }

    /// <summary>Posición en el reporte del worker, desde 0.</summary>
    public int Orden { get; private set; }

    public RegionDeImagen Region { get; private set; }

    /// <summary>Clase que asignó el modelo (detección); null en un hallazgo de texto.</summary>
    public string? Clase { get; private set; }

    /// <summary>Lo que el modelo leyó (OCR); null en una detección. Nunca un dato verificado.</summary>
    public string? TextoLeido { get; private set; }

    public decimal Confianza { get; private set; }

    /// <summary>La salida del modelo para este hallazgo, en JSON tal como la reportó el worker.</summary>
    public string DatoCrudo { get; private set; }

    public ViaProcesamiento Via => ViaProcesamiento.SoloInvestigacion;

    internal static Hallazgo Desde(Guid analisisId, int orden, HallazgoPropuesto propuesto) => new()
    {
        Id = Guid.NewGuid(),
        AnalisisId = analisisId,
        Orden = orden,
        Region = propuesto.Region,
        Clase = propuesto.Clase,
        TextoLeido = propuesto.TextoLeido,
        Confianza = propuesto.Confianza,
        DatoCrudo = propuesto.DatoCrudo,
    };
}
