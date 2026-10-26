using Forensic.Application;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Operaciones;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Forensic.Infrastructure.Persistencia;

internal sealed class PersistenciaForense(ContextoForense db) : IPersistenciaForense
{
    public Task<Caso?> ObtenerCasoAsync(Guid casoId, CancellationToken ct) =>
        db.Casos.SingleOrDefaultAsync(c => c.Id == casoId, ct);

    public async Task<IReadOnlyList<Caso>> ListarCasosAsync(CancellationToken ct) =>
        await db.Casos.AsNoTracking().OrderByDescending(c => c.AbiertoEn).ToListAsync(ct);

    public Task<Evidencia?> ObtenerEvidenciaAsync(Guid casoId, Guid evidenciaId, CancellationToken ct) =>
        db.Evidencias.AsNoTracking().SingleOrDefaultAsync(e => e.CasoId == casoId && e.Id == evidenciaId, ct);

    public async Task<IReadOnlyList<Evidencia>> ListarEvidenciasAsync(Guid casoId, CancellationToken ct) =>
        await db.Evidencias.AsNoTracking().Where(e => e.CasoId == casoId).OrderBy(e => e.IngestadaEn).ToListAsync(ct);

    public Task<AsientoDeCustodia?> ObtenerUltimoAsientoAsync(Guid casoId, CancellationToken ct) =>
        db.AsientosDeCustodia.AsNoTracking()
            .Where(a => a.CasoId == casoId)
            .OrderByDescending(a => a.Secuencia)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AsientoDeCustodia>> ObtenerCadenaAsync(Guid casoId, CancellationToken ct) =>
        await db.AsientosDeCustodia.AsNoTracking()
            .Where(a => a.CasoId == casoId)
            .OrderBy(a => a.Secuencia)
            .ToListAsync(ct);

    public void Agregar(Caso caso) => db.Casos.Add(caso);
    public void Agregar(Evidencia evidencia) => db.Evidencias.Add(evidencia);
    public void Agregar(AsientoDeCustodia asiento) => db.AsientosDeCustodia.Add(asiento);
    public void Agregar(OperacionForense operacion) => db.OperacionesForenses.Add(operacion);

    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try
        {
            // SaveChanges ya envuelve todo en una transacción: entidad y asiento entran juntos o no entran.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_AsientosDeCustodia_CasoId_Secuencia",
        })
        {
            db.ChangeTracker.Clear();
            throw new ConflictoDeCustodiaException(
                "Otro asiento se registró en la misma posición de la cadena. Reintente la operación.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Casos_NumeroExpediente",
        })
        {
            db.ChangeTracker.Clear();
            throw new ExcepcionDeDominio("Ya existe un caso con ese número de expediente.");
        }
    }
}
