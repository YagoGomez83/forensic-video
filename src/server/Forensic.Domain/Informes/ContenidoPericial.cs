using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Informes;

/// <summary>Una evidencia que pasó la puerta del informe pericial, con el asiento que la registró.</summary>
public sealed record EvidenciaDelInforme(Evidencia Evidencia, long AsientoDeOrigen);

/// <summary>Un asiento de la cadena tal como entra al informe: completo, u opaco (sólo su enlace de hashes).</summary>
public sealed record AsientoDelInforme(AsientoDeCustodia Asiento, bool Opaco);

/// <summary>
/// Lo que puede contener un informe pericial, decidido por construcción:
/// <list type="bullet">
/// <item>Una evidencia entra sólo si pasa <see cref="Evidencia.ExigirAdmisibleEnJuicio"/>, la única
/// puerta: no hay otro predicado de vía.</item>
/// <item>La cadena entra entera (sin ella no se verifica), pero un asiento se muestra completo sólo si
/// su tipo está en la lista blanca cerrada <see cref="TiposCompletos"/> y, si refiere a una evidencia,
/// ésa entró. Todo lo demás va opaco: lo de IA, lo que toca a un derivado «sólo investigación» y
/// cualquier tipo de asiento que se agregue en el futuro.</item>
/// </list>
/// La lista blanca es la de docs/formato-bundle-v1.md; el verificador independiente aplica la misma.
/// </summary>
public sealed class ContenidoPericial
{
    public static IReadOnlySet<TipoAsiento> TiposCompletos { get; } = new HashSet<TipoAsiento>
    {
        TipoAsiento.CasoAbierto,
        TipoAsiento.AnalistaAsignado,
        TipoAsiento.CasoCerrado,
        TipoAsiento.EvidenciaIngestada,
        TipoAsiento.EvidenciaAccedida,
        TipoAsiento.ControlCruzadoCompletado,
        TipoAsiento.OperacionAplicada,
        TipoAsiento.OperacionReproducida,
        TipoAsiento.InformePericialEmitido,
    };

    private ContenidoPericial(
        IReadOnlyList<EvidenciaDelInforme> evidencias, IReadOnlyList<Guid> excluidas, IReadOnlyList<AsientoDelInforme> asientos)
    {
        Evidencias = evidencias;
        EvidenciasExcluidas = excluidas;
        Asientos = asientos;
        AsientosOpacos = asientos.Count(a => a.Opaco);
    }

    /// <summary>Las evidencias que el informe presenta, en el orden en que se registraron.</summary>
    public IReadOnlyList<EvidenciaDelInforme> Evidencias { get; }

    /// <summary>Las evidencias «sólo investigación» del caso, que el informe no incluye.</summary>
    public IReadOnlyList<Guid> EvidenciasExcluidas { get; }

    /// <summary>La cadena completa, en orden de secuencia.</summary>
    public IReadOnlyList<AsientoDelInforme> Asientos { get; }

    public int AsientosOpacos { get; }

    public static bool EsOpaco(TipoAsiento tipo, Guid? evidencia, IReadOnlySet<Guid> admitidas) =>
        !TiposCompletos.Contains(tipo) || evidencia is { } id && !admitidas.Contains(id);

    public static ContenidoPericial Armar(Caso caso, IReadOnlyList<Evidencia> evidencias, IReadOnlyList<AsientoDeCustodia> cadena)
    {
        if (cadena.Count == 0)
            throw new ExcepcionDeDominio("Un caso sin cadena de custodia no tiene nada que informar.");
        if (cadena.Any(a => a.CasoId != caso.Id) || evidencias.Any(e => e.CasoId != caso.Id))
            throw new ExcepcionDeDominio("El informe de un caso no puede incluir asientos ni evidencias de otro.");

        var admitidas = new List<Evidencia>();
        var excluidas = new List<Guid>();
        foreach (var e in evidencias)
        {
            if (Admitir(e))
                admitidas.Add(e);
            else
                excluidas.Add(e.Id);
        }

        var ids = admitidas.Select(e => e.Id).ToHashSet();
        var conOrigen = admitidas.Select(e => new EvidenciaDelInforme(e, AsientoDeOrigen(e, cadena))).ToList();
        var asientos = cadena.Select(a => new AsientoDelInforme(a, EsOpaco(a.Tipo, a.EvidenciaId, ids))).ToList();
        return new ContenidoPericial(conOrigen, excluidas, asientos);
    }

    private static bool Admitir(Evidencia evidencia)
    {
        try
        {
            evidencia.ExigirAdmisibleEnJuicio();
            return true;
        }
        catch (EvidenciaSoloInvestigacionException)
        {
            return false;
        }
    }

    private static long AsientoDeOrigen(Evidencia e, IReadOnlyList<AsientoDeCustodia> cadena)
    {
        var tipo = e.EsOriginal ? TipoAsiento.EvidenciaIngestada : TipoAsiento.OperacionAplicada;
        return cadena.SingleOrDefault(a => a.Tipo == tipo && a.EvidenciaId == e.Id)?.Secuencia
            ?? throw new ExcepcionDeDominio(
                $"La evidencia {e.Id} no tiene su asiento {tipo} en la cadena: no puede presentarse.");
    }
}
