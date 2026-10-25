namespace Forensic.Domain.Evidencias;

/// <summary>
/// Doble vía de procesamiento. Los valores empiezan en 1 para que un <c>default</c> no pase
/// silenciosamente por «admisible».
/// </summary>
public enum ViaProcesamiento
{
    /// <summary>Original o derivado sólo por operaciones deterministas: puede presentarse en juicio.</summary>
    AdmisibleEnJuicio = 1,

    /// <summary>Tocado por IA en algún punto de su linaje: nunca puede presentarse como prueba.</summary>
    SoloInvestigacion = 2,
}

public static class ViaProcesamientoExtensiones
{
    /// <summary>La marca «sólo investigación» es absorbente: se hereda a todo descendiente.</summary>
    public static ViaProcesamiento Propagar(this ViaProcesamiento padre, ViaProcesamiento operacion) =>
        padre == ViaProcesamiento.SoloInvestigacion || operacion == ViaProcesamiento.SoloInvestigacion
            ? ViaProcesamiento.SoloInvestigacion
            : ViaProcesamiento.AdmisibleEnJuicio;
}
