using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Forensic.Api.Controllers;
using Forensic.Application.Casos;
using Forensic.Application.Custodia;
using Forensic.Application.Evidencias;
using Forensic.Domain.Comun;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>Gate de la Fase 1: una evidencia se ingesta, hashea y audita de punta a punta.</summary>
public class CustodiaDePuntaAPuntaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    [Fact]
    public async Task Ingesta_descarga_verificada_y_export_de_una_cadena_integra()
    {
        var caso = await _ana.AbrirCasoAsync();
        var bytes = RandomNumberGenerator.GetBytes(256 * 1024);
        var shaEsperado = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var evidencia = await (await _ana.IngestarAsync(caso.Id, bytes)).LeerAsync<EvidenciaDto>();
        Assert.Equal(shaEsperado, evidencia.Hash);
        Assert.Equal(bytes.Length, evidencia.TamanoBytes);
        Assert.Equal("cámara 1.mp4", evidencia.NombreOriginal);

        var descarga = await _ana.GetAsync($"/casos/{caso.Id}/evidencias/{evidencia.Id}/contenido");
        descarga.EnsureSuccessStatusCode();
        Assert.Equal(bytes, await descarga.Content.ReadAsByteArrayAsync());
        Assert.Equal(shaEsperado, descarga.Headers.GetValues("X-Evidencia-Sha256").Single());

        var export = await (await _ana.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        Assert.Equal(["CasoAbierto", "EvidenciaIngestada", "EvidenciaAccedida"], export.Asientos.Select(a => a.Tipo));
        Assert.All(export.Asientos, a => Assert.Equal("ana", a.Actor));
        Assert.Equal(export.Asientos[^1].Hash, export.Verificacion.HashFinal);
        Assert.Contains(shaEsperado, export.Asientos[1].Detalle);
    }

    [Fact]
    public async Task Un_analista_no_asignado_no_puede_ingestar_ni_leer()
    {
        var caso = await _ana.AbrirCasoAsync();
        var beto = servidor.ClienteDe("beto", Rol.Analista);

        Assert.Equal(HttpStatusCode.Forbidden, (await beto.IngestarAsync(caso.Id, [1, 2, 3])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await beto.GetAsync($"/casos/{caso.Id}/custodia")).StatusCode);
    }

    [Fact]
    public async Task Sin_identidad_no_hay_acceso()
    {
        var anonimo = servidor.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/casos")).StatusCode);
    }

    [Fact]
    public async Task Un_blob_alterado_en_disco_bloquea_la_evidencia()
    {
        var caso = await _ana.AbrirCasoAsync();
        var evidencia = await (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096))).LeerAsync<EvidenciaDto>();

        var ruta = Path.Combine(servidor.RutaAlmacen, evidencia.Hash[..2], evidencia.Hash[2..4], evidencia.Hash);
        File.SetAttributes(ruta, FileAttributes.Normal);
        await File.WriteAllBytesAsync(ruta, RandomNumberGenerator.GetBytes(4096));

        var r = await _ana.GetAsync($"/casos/{caso.Id}/evidencias/{evidencia.Id}/contenido");

        Assert.Equal(HttpStatusCode.Locked, r.StatusCode);
    }

    [Fact]
    public async Task La_base_rechaza_modificar_o_borrar_asientos()
    {
        var caso = await _ana.AbrirCasoAsync();
        await using var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();

        foreach (var sql in new[]
        {
            """UPDATE "AsientosDeCustodia" SET "Actor" = 'otro' WHERE "CasoId" = @caso""",
            """DELETE FROM "AsientosDeCustodia" WHERE "CasoId" = @caso""",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("caso", caso.Id);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Contains("append-only", ex.MessageText);
        }
    }

    [Fact]
    public async Task Una_manipulacion_con_triggers_desactivados_se_detecta_al_exportar()
    {
        var caso = await _ana.AbrirCasoAsync();
        await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(1024));

        // Un superusuario puede saltear los triggers; la hash-chain igual lo delata.
        await using (var conexion = new NpgsqlConnection(servidor.CadenaDeConexion))
        {
            await conexion.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                ALTER TABLE "AsientosDeCustodia" DISABLE TRIGGER "AsientosDeCustodia_append_only";
                UPDATE "AsientosDeCustodia" SET "Actor" = 'impostor' WHERE "CasoId" = @caso AND "Secuencia" = 2;
                ALTER TABLE "AsientosDeCustodia" ENABLE TRIGGER "AsientosDeCustodia_append_only";
                """, conexion);
            cmd.Parameters.AddWithValue("caso", caso.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        var export = await (await _ana.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();

        Assert.False(export.Verificacion.EsIntegra);
        Assert.Equal(2, export.Verificacion.SecuenciaRota);
    }

    [Fact]
    public async Task Asignar_analista_y_cerrar_quedan_en_la_cadena()
    {
        var caso = await _ana.AbrirCasoAsync();
        var sofia = servidor.ClienteDe("sofia", Rol.Supervisor);

        (await sofia.PostAsJsonAsync($"/casos/{caso.Id}/analistas", new AsignarAnalistaRequest("beto"))).EnsureSuccessStatusCode();
        (await sofia.PostAsync($"/casos/{caso.Id}/cerrar", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _ana.PostAsJsonAsync("/casos", new AbrirCasoRequest(caso.NumeroExpediente, "duplicado"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _ana.IngestarAsync(caso.Id, [1])).StatusCode);

        var export = await (await sofia.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();
        Assert.True(export.Verificacion.EsIntegra);
        Assert.Equal(["CasoAbierto", "AnalistaAsignado", "CasoCerrado"], export.Asientos.Select(a => a.Tipo));
    }
}
