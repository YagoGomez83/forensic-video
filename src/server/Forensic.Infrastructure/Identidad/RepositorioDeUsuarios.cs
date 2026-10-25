using Forensic.Application;
using Forensic.Application.Identidad;
using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Forensic.Infrastructure.Identidad;

/// <summary>
/// Usuarios y auditoría de accesos sobre el mismo <see cref="ContextoDeIdentidad"/>: un cambio y su
/// evento de auditoría se guardan en la misma transacción.
/// </summary>
internal sealed class RepositorioDeUsuarios(ContextoDeIdentidad db) : IRepositorioDeUsuarios, IAuditoriaDeAccesos
{
    public Task<Usuario?> ObtenerAsync(string nombre, CancellationToken ct) =>
        db.Usuarios.SingleOrDefaultAsync(u => u.Nombre == nombre, ct);

    public async Task<IReadOnlyList<Usuario>> ListarAsync(CancellationToken ct) =>
        await db.Usuarios.AsNoTracking().OrderBy(u => u.Nombre).ToListAsync(ct);

    public Task<bool> ExisteAdminActivoAsync(CancellationToken ct) =>
        db.Usuarios.AnyAsync(u => u.Rol == Rol.Admin && u.Activo, ct);

    public void Agregar(Usuario usuario) => db.Usuarios.Add(usuario);

    public void Registrar(EventoDeAcceso evento) => db.AuditoriaDeAccesos.Add(new RegistroDeAcceso
    {
        Tipo = evento.Tipo,
        Usuario = Recortar(evento.Usuario, 100),
        Actor = Recortar(evento.Actor, 100),
        Detalle = evento.Detalle,
        Origen = evento.Origen,
        Timestamp = Tiempo.NormalizarParaCustodia(evento.Timestamp),
    });

    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            throw new ConflictoDeIdentidadException("Otro proceso modificó el usuario. Reintente la operación.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Usuarios_Nombre",
        })
        {
            db.ChangeTracker.Clear();
            throw new ExcepcionDeDominio("Ya existe un usuario con ese nombre.");
        }
    }

    /// <summary>Un login puede traer cualquier texto como usuario; se audita truncado.</summary>
    private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];
}
