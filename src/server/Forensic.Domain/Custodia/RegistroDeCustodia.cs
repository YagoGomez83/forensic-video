using Forensic.Domain.Comun;

namespace Forensic.Domain.Custodia;

/// <summary>
/// Cadena de custodia de un caso: append-only y encadenada por hash. Para asentar sólo hace falta
/// el último eslabón; para verificar, la cadena completa.
/// </summary>
/// <remarks>
/// La cadena detecta alteración o eliminación de asientos intermedios, pero no el truncado del final.
/// Eso lo cubre la firma periódica del estado de la cadena (Fase 5) y, mientras tanto, los
/// triggers que impiden UPDATE/DELETE en la base.
/// </remarks>
public static class RegistroDeCustodia
{
    public static AsientoDeCustodia Asentar(
        AsientoDeCustodia? ultimo,
        Guid casoId,
        TipoAsiento tipo,
        Guid? evidenciaId,
        string detalle,
        Actor actor,
        DateTimeOffset ahora)
    {
        if (ultimo is not null && ultimo.CasoId != casoId)
            throw new ExcepcionDeDominio("El último asiento pertenece a otro caso.");
        if (!Enum.IsDefined(tipo))
            throw new ExcepcionDeDominio("Tipo de asiento inválido.");
        if (ultimo is not null && Tiempo.NormalizarParaCustodia(ahora) < ultimo.Timestamp)
            throw new ExcepcionDeDominio("El reloj del servidor retrocedió respecto del último asiento.");

        return AsientoDeCustodia.Crear(
            casoId,
            secuencia: (ultimo?.Secuencia ?? 0) + 1,
            tipo,
            evidenciaId,
            detalle ?? "{}",
            actor.Usuario,
            ahora,
            hashAnterior: ultimo?.Hash ?? HashSha256.Genesis);
    }

    /// <summary>Verifica una cadena completa, ordenada por secuencia.</summary>
    public static VerificacionDeCadena Verificar(Guid casoId, IReadOnlyList<AsientoDeCustodia> asientos)
    {
        var anterior = HashSha256.Genesis;
        for (var i = 0; i < asientos.Count; i++)
        {
            var a = asientos[i];
            var esperada = i + 1L;

            if (a.CasoId != casoId)
                return VerificacionDeCadena.Rota(a.Secuencia, "El asiento pertenece a otro caso.");
            if (a.Secuencia != esperada)
                return VerificacionDeCadena.Rota(a.Secuencia, $"Secuencia {a.Secuencia} donde se esperaba {esperada}: falta o sobra un asiento.");
            if (a.HashAnterior != anterior)
                return VerificacionDeCadena.Rota(a.Secuencia, "El hash anterior no coincide con el asiento previo.");
            if (a.CalcularHash() != a.Hash)
                return VerificacionDeCadena.Rota(a.Secuencia, "El contenido del asiento no coincide con su hash: fue alterado.");

            anterior = a.Hash;
        }
        return VerificacionDeCadena.Integra(asientos.Count, anterior);
    }
}

public sealed record VerificacionDeCadena(
    bool EsIntegra, int CantidadAsientos, HashSha256? HashFinal, long? SecuenciaRota, string? Motivo)
{
    internal static VerificacionDeCadena Integra(int cantidad, HashSha256 hashFinal) =>
        new(true, cantidad, hashFinal, null, null);

    internal static VerificacionDeCadena Rota(long secuencia, string motivo) =>
        new(false, 0, null, secuencia, motivo);
}
