using System.Text.Json;
using Forensic.Domain.Custodia;

namespace Forensic.Application.Custodia;

/// <summary>
/// Estado del control cruzado de ingesta. Las tablas de custodia son inmutables, así que se deduce
/// de la cadena: el asiento de ingesta lo dejó pendiente y todavía no hay un asiento que lo complete.
/// </summary>
public static class ControlesCruzados
{
    public const string Pendiente = "pendiente";

    /// <summary>Evidencias del caso cuyo control cruzado sigue pendiente.</summary>
    public static IReadOnlySet<Guid> Pendientes(IReadOnlyList<AsientoDeCustodia> cadena)
    {
        var completados = cadena
            .Where(a => a.Tipo == TipoAsiento.ControlCruzadoCompletado && a.EvidenciaId is not null)
            .Select(a => a.EvidenciaId!.Value)
            .ToHashSet();
        return cadena
            .Where(a => a.Tipo == TipoAsiento.EvidenciaIngestada && a.EvidenciaId is { } id
                && !completados.Contains(id) && QuedoPendiente(a))
            .Select(a => a.EvidenciaId!.Value)
            .ToHashSet();
    }

    private static bool QuedoPendiente(AsientoDeCustodia ingesta)
    {
        using var detalle = JsonDocument.Parse(ingesta.Detalle);
        return detalle.RootElement.TryGetProperty("controlCruzado", out var control)
            && control.TryGetProperty("estado", out var estado)
            && estado.GetString() == Pendiente;
    }
}
