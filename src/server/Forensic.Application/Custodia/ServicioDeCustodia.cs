using Forensic.Application.Casos;
using Forensic.Domain.Custodia;

namespace Forensic.Application.Custodia;

public sealed record AsientoDto(
    long Secuencia, Guid Id, string Tipo, Guid? EvidenciaId, string Detalle, string Actor,
    DateTimeOffset Timestamp, string HashAnterior, string Hash);

public sealed record VerificacionDto(bool EsIntegra, int CantidadAsientos, string? HashFinal, long? SecuenciaRota, string? Motivo);

/// <summary>
/// Export del registro de custodia de un caso: todos los asientos con sus hashes y el resultado
/// de verificar la cadena. Base del informe pericial; un tercero puede reverificarlo sin el código
/// siguiendo el formato canónico documentado en <see cref="AsientoDeCustodia"/>.
/// </summary>
public sealed record ExportDeCustodia(
    string Formato, DateTimeOffset GeneradoEn, string GeneradoPor, CasoDto Caso,
    IReadOnlyList<AsientoDto> Asientos, VerificacionDto Verificacion);

public sealed class ServicioDeCustodia(IPersistenciaForense persistencia, IActorActual actorActual, TimeProvider reloj)
{
    public async Task<ExportDeCustodia> ExportarAsync(Guid casoId, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await persistencia.ObtenerCasoAsync(casoId, ct)
            ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
        caso.ExigirLectura(actor);

        var cadena = await persistencia.ObtenerCadenaAsync(casoId, ct);
        var verificacion = RegistroDeCustodia.Verificar(casoId, cadena);

        return new ExportDeCustodia(
            AsientoDeCustodia.FormatoCanonico,
            reloj.GetUtcNow(),
            actor.Usuario,
            CasoDto.Desde(caso),
            cadena.Select(a => new AsientoDto(
                a.Secuencia, a.Id, a.Tipo.ToString(), a.EvidenciaId, a.Detalle, a.Actor,
                a.Timestamp, a.HashAnterior.Hex, a.Hash.Hex)).ToList(),
            new VerificacionDto(
                verificacion.EsIntegra, verificacion.CantidadAsientos, verificacion.HashFinal?.Hex,
                verificacion.SecuenciaRota, verificacion.Motivo));
    }
}
