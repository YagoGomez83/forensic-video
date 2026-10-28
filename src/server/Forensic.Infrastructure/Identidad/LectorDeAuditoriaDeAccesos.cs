using Forensic.Application.Identidad;
using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;
using Microsoft.EntityFrameworkCore;

namespace Forensic.Infrastructure.Identidad;

internal sealed class LectorDeAuditoriaDeAccesos(ContextoDeIdentidad db) : ILectorDeAuditoriaDeAccesos
{
    public async Task<IReadOnlyList<AccesoEncadenado>> LeerCadenaAsync(CancellationToken ct) =>
        (await db.AuditoriaDeAccesos.AsNoTracking().OrderBy(r => r.Secuencia).ToListAsync(ct))
            .Select(r => new AccesoEncadenado(
                r.Secuencia, r.Tipo.ToString(), r.Usuario, r.Actor, r.Detalle, r.Origen, r.Timestamp,
                HashSha256.Parse(r.HashAnterior), HashSha256.Parse(r.Hash)))
            .ToList();
}
