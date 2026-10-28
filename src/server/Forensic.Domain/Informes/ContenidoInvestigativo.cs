using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Informes;

/// <summary>
/// Lo que contiene un informe investigativo: todas las evidencias del caso, de las dos vías (el
/// material de IA necesita su linaje admisible para verificarse), y la cadena entera con todos sus
/// asientos completos. Todo el informe es «sólo investigación» (<see cref="Via"/>): nunca es prueba.
/// Es un tipo aparte de <see cref="ContenidoPericial"/> a propósito: no hay conversión entre ellos.
/// </summary>
public sealed class ContenidoInvestigativo
{
    private ContenidoInvestigativo(IReadOnlyList<EvidenciaDelInforme> evidencias, IReadOnlyList<AsientoDelInforme> asientos)
    {
        Evidencias = evidencias;
        Asientos = asientos;
    }

    /// <summary>La vía del informe entero, sea cual sea la de cada evidencia que contiene.</summary>
    public ViaProcesamiento Via => ViaProcesamiento.SoloInvestigacion;

    public IReadOnlyList<EvidenciaDelInforme> Evidencias { get; }

    public IReadOnlyList<AsientoDelInforme> Asientos { get; }

    public static ContenidoInvestigativo Armar(Caso caso, IReadOnlyList<Evidencia> evidencias, IReadOnlyList<AsientoDeCustodia> cadena)
    {
        if (cadena.Count == 0)
            throw new ExcepcionDeDominio("Un caso sin cadena de custodia no tiene nada que informar.");
        if (cadena.Any(a => a.CasoId != caso.Id) || evidencias.Any(e => e.CasoId != caso.Id))
            throw new ExcepcionDeDominio("El informe de un caso no puede incluir asientos ni evidencias de otro.");

        var conOrigen = evidencias.Select(e => new EvidenciaDelInforme(e, AsientoDeOrigen(e, cadena))).ToList();
        return new ContenidoInvestigativo(conOrigen, cadena.Select(a => new AsientoDelInforme(a, Opaco: false)).ToList());
    }

    private static long AsientoDeOrigen(Evidencia e, IReadOnlyList<AsientoDeCustodia> cadena)
    {
        var asiento = e.EsOriginal
            ? cadena.SingleOrDefault(a => a.Tipo == TipoAsiento.EvidenciaIngestada && a.EvidenciaId == e.Id)
            : cadena.SingleOrDefault(a => a.Tipo is TipoAsiento.OperacionAplicada or TipoAsiento.OperacionIaAplicada && a.EvidenciaId == e.Id);
        return asiento?.Secuencia
            ?? throw new ExcepcionDeDominio($"La evidencia {e.Id} no tiene en la cadena el asiento que la registró.");
    }
}
