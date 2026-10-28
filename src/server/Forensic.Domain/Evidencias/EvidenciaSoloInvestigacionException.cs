using Forensic.Domain.Comun;

namespace Forensic.Domain.Evidencias;

/// <summary>
/// Se intentó presentar como prueba una evidencia «sólo investigación». La lanza únicamente
/// <see cref="Evidencia.ExigirAdmisibleEnJuicio"/>, la única puerta del informe pericial.
/// </summary>
public sealed class EvidenciaSoloInvestigacionException(Guid evidenciaId, string mensaje) : ExcepcionDeDominio(mensaje)
{
    public Guid EvidenciaId { get; } = evidenciaId;
}
