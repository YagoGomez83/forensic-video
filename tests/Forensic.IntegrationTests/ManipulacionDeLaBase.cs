using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>
/// Lo que puede hacer quien controla PostgreSQL: saltear los triggers append-only y reescribir la
/// cadena. Sirve para probar que la manipulación se detecta.
/// </summary>
internal static class ManipulacionDeLaBase
{
    public static async Task EjecutarSinTriggersAsync(this ServidorForense servidor, string sql, Guid casoId)
    {
        await using var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
            ALTER TABLE "AsientosDeCustodia" DISABLE TRIGGER "AsientosDeCustodia_append_only";
            {sql}
            ALTER TABLE "AsientosDeCustodia" ENABLE TRIGGER "AsientosDeCustodia_append_only";
            """, conexion);
        cmd.Parameters.AddWithValue("caso", casoId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Borra la cadena del caso y la vuelve a asentar entera, con el actor del asiento 2 cambiado y
    /// todos los hashes recalculados. La cadena nueva verifica por sí misma; sólo un sello la delata.
    /// </summary>
    public static async Task ReconstruirCadenaAsync(this ServidorForense servidor, Guid casoId)
    {
        var original = (await servidor.ClienteDe("ana", Rol.Analista).ExportarAsync(casoId)).Asientos;
        await servidor.EjecutarSinTriggersAsync("""DELETE FROM "AsientosDeCustodia" WHERE "CasoId" = @caso;""", casoId);

        using var scope = servidor.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContextoForense>();
        AsientoDeCustodia? ultimo = null;
        foreach (var a in original)
        {
            var actor = a.Secuencia == 2 ? "impostor" : a.Actor;
            ultimo = RegistroDeCustodia.Asentar(
                ultimo, casoId, Enum.Parse<TipoAsiento>(a.Tipo), a.EvidenciaId, a.Detalle,
                new Actor(actor, Rol.Analista), a.Timestamp);
            db.AsientosDeCustodia.Add(ultimo);
        }
        await db.SaveChangesAsync();
    }
}
