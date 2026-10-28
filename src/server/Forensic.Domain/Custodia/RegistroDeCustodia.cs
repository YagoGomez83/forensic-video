using Forensic.Domain.Comun;

namespace Forensic.Domain.Custodia;

/// <summary>
/// Cadena de custodia de un caso: append-only y encadenada por hash. Para asentar sólo hace falta
/// el último eslabón; para verificar, la cadena completa.
/// </summary>
/// <remarks>
/// La cadena detecta alteración o eliminación de asientos intermedios, pero no el truncado del final
/// ni una reconstrucción entera con todos los hashes recalculados. Eso lo cubren los
/// <see cref="SelloDeCadena"/> (<see cref="VerificarSellos"/>), hasta el último asiento sellado.
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

    /// <summary>
    /// Cruza la cadena (ordenada por secuencia) con sus sellos. Usa los hashes guardados en cada
    /// asiento: que además se recalculen bien es <see cref="Verificar"/>. Una cadena reconstruida
    /// entera pasa <see cref="Verificar"/>, pero no coincide con un sello anterior.
    /// </summary>
    public static VerificacionDeSellos VerificarSellos(
        Guid casoId, IReadOnlyList<AsientoDeCustodia> asientos, IReadOnlyList<SelloDeCadena> sellos,
        Func<SelloDeCadena, bool> firmaValida)
    {
        foreach (var s in sellos.OrderBy(s => s.Secuencia))
        {
            if (s.CasoId != casoId)
                return SelloRoto(sellos, s, "El sello pertenece a otro caso.");
            if (!firmaValida(s))
                return SelloRoto(sellos, s, "La firma del sello no es válida para la clave del laboratorio.");
            if (s.Secuencia < 1 || s.Secuencia > asientos.Count)
                return SelloRoto(sellos, s, $"El sello cubre la secuencia {s.Secuencia}, pero la cadena tiene {asientos.Count} asientos: fue truncada.");
            var sellado = asientos[(int)(s.Secuencia - 1)];
            if (sellado.Secuencia != s.Secuencia || sellado.Hash != s.HashCabeza)
                return SelloRoto(sellos, s, "El asiento sellado no tiene el hash del sello: la cadena fue reconstruida o alterada.");
        }

        long? ultima = sellos.Count == 0 ? null : sellos.Max(s => s.Secuencia);
        return new VerificacionDeSellos(true, sellos.Count, ultima, asientos.Count - (ultima ?? 0), null, null);
    }

    private static VerificacionDeSellos SelloRoto(IReadOnlyList<SelloDeCadena> sellos, SelloDeCadena roto, string motivo) =>
        new(false, sellos.Count, null, 0, roto.Secuencia, motivo);
}

public sealed record VerificacionDeCadena(
    bool EsIntegra, int CantidadAsientos, HashSha256? HashFinal, long? SecuenciaRota, string? Motivo)
{
    internal static VerificacionDeCadena Integra(int cantidad, HashSha256 hashFinal) =>
        new(true, cantidad, hashFinal, null, null);

    internal static VerificacionDeCadena Rota(long secuencia, string motivo) =>
        new(false, 0, null, secuencia, motivo);
}
