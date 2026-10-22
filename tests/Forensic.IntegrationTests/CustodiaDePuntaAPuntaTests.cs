using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Forensic.Api.Controllers;
using Forensic.Application;
using Forensic.Application.Casos;
using Forensic.Application.Custodia;
using Forensic.Application.Evidencias;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>Gate de la Fase 1: una evidencia se ingesta, hashea y audita de punta a punta.</summary>
public class CustodiaDePuntaAPuntaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    private static string Expediente() => $"EXP-{Guid.NewGuid():N}"[..20];

    private async Task<CasoDto> AbrirCasoAsync() =>
        await (await _ana.PostAsJsonAsync("/casos", new AbrirCasoRequest(Expediente(), "Prueba"))).LeerAsync<CasoDto>();

    private static async Task<HttpResponseMessage> IngestarAsync(HttpClient cliente, Guid casoId, byte[] bytes, string nombre = "cámara 1.mp4")
    {
        var contenido = new ByteArrayContent(bytes);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await cliente.PostAsync($"/casos/{casoId}/evidencias?nombre={Uri.EscapeDataString(nombre)}", contenido);
    }

    [Fact]
    public async Task Ingesta_descarga_verificada_y_export_de_una_cadena_integra()
    {
        var caso = await AbrirCasoAsync();
        var bytes = RandomNumberGenerator.GetBytes(256 * 1024);
        var shaEsperado = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var evidencia = await (await IngestarAsync(_ana, caso.Id, bytes)).LeerAsync<EvidenciaDto>();
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
    public async Task Ingesta_se_rechaza_si_el_hash_del_worker_no_coincide()
    {
        var caso = await AbrirCasoAsync();
        await using var conWorkerDiscrepante = servidor.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IWorkerDeIngesta>(new WorkerDeIngestaDoble(discrepar: true))));
        var ana = conWorkerDiscrepante.CreateClient().ComoActor("ana", Rol.Analista);

        var r = await IngestarAsync(ana, caso.Id, RandomNumberGenerator.GetBytes(4096));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Empty(await (await _ana.GetAsync($"/casos/{caso.Id}/evidencias")).LeerAsync<List<EvidenciaDto>>());
        var export = await (await _ana.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        Assert.Equal(["CasoAbierto"], export.Asientos.Select(a => a.Tipo));
    }

    [Fact]
    public async Task La_ingesta_asienta_el_control_cruzado_y_los_metadatos()
    {
        var caso = await AbrirCasoAsync();

        var evidencia = await (await IngestarAsync(_ana, caso.Id, RandomNumberGenerator.GetBytes(2048))).LeerAsync<EvidenciaDto>();

        var export = await (await _ana.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();
        using var detalle = JsonDocument.Parse(export.Asientos[1].Detalle);
        var raiz = detalle.RootElement;
        Assert.Equal(evidencia.Hash, raiz.GetProperty("controlCruzado").GetProperty("sha256Worker").GetString());
        Assert.Equal("doble", raiz.GetProperty("metadatos").GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, raiz.GetProperty("motivoSinMetadatos").ValueKind);
    }

    [Fact]
    public async Task Una_evidencia_que_ffprobe_no_lee_se_custodia_igual()
    {
        var caso = await AbrirCasoAsync();
        await using var sinMetadatos = servidor.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IWorkerDeIngesta>(new WorkerDeIngestaDoble(motivoSinMetadatos: "formato no reconocido"))));
        var ana = sinMetadatos.CreateClient().ComoActor("ana", Rol.Analista);

        var r = await IngestarAsync(ana, caso.Id, RandomNumberGenerator.GetBytes(2048), "export.dav");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var export = await (await _ana.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();
        using var detalle = JsonDocument.Parse(export.Asientos[1].Detalle);
        Assert.Equal(JsonValueKind.Null, detalle.RootElement.GetProperty("metadatos").ValueKind);
        Assert.Equal("formato no reconocido", detalle.RootElement.GetProperty("motivoSinMetadatos").GetString());
    }

    [Fact]
    public async Task Sin_worker_de_ingesta_no_hay_ingesta()
    {
        var caso = await AbrirCasoAsync();
        // El cliente HTTP real contra un puerto donde no escucha nadie.
        await using var sinWorker = servidor.WithWebHostBuilder(b =>
        {
            b.UseSetting("WorkerIngesta:UrlBase", "http://127.0.0.1:9");
            b.ConfigureTestServices(s => s.AddHttpClient<IWorkerDeIngesta, WorkerDeIngestaHttp>());
        });
        var ana = sinWorker.CreateClient().ComoActor("ana", Rol.Analista);

        var r = await IngestarAsync(ana, caso.Id, RandomNumberGenerator.GetBytes(2048));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Empty(await (await _ana.GetAsync($"/casos/{caso.Id}/evidencias")).LeerAsync<List<EvidenciaDto>>());
        var export = await (await _ana.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();
        Assert.Equal(["CasoAbierto"], export.Asientos.Select(a => a.Tipo));
    }

    [Fact]
    public async Task Un_analista_no_asignado_no_puede_ingestar_ni_leer()
    {
        var caso = await AbrirCasoAsync();
        var beto = servidor.ClienteDe("beto", Rol.Analista);

        Assert.Equal(HttpStatusCode.Forbidden, (await IngestarAsync(beto, caso.Id, [1, 2, 3])).StatusCode);
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
        var caso = await AbrirCasoAsync();
        var evidencia = await (await IngestarAsync(_ana, caso.Id, RandomNumberGenerator.GetBytes(4096))).LeerAsync<EvidenciaDto>();

        var ruta = Path.Combine(servidor.RutaAlmacen, evidencia.Hash[..2], evidencia.Hash[2..4], evidencia.Hash);
        File.SetAttributes(ruta, FileAttributes.Normal);
        await File.WriteAllBytesAsync(ruta, RandomNumberGenerator.GetBytes(4096));

        var r = await _ana.GetAsync($"/casos/{caso.Id}/evidencias/{evidencia.Id}/contenido");

        Assert.Equal(HttpStatusCode.Locked, r.StatusCode);
    }

    [Fact]
    public async Task La_base_rechaza_modificar_o_borrar_asientos()
    {
        var caso = await AbrirCasoAsync();
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
        var caso = await AbrirCasoAsync();
        await IngestarAsync(_ana, caso.Id, RandomNumberGenerator.GetBytes(1024));

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
        var caso = await AbrirCasoAsync();
        var sofia = servidor.ClienteDe("sofia", Rol.Supervisor);

        (await sofia.PostAsJsonAsync($"/casos/{caso.Id}/analistas", new AsignarAnalistaRequest("beto"))).EnsureSuccessStatusCode();
        (await sofia.PostAsync($"/casos/{caso.Id}/cerrar", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _ana.PostAsJsonAsync("/casos", new AbrirCasoRequest(caso.NumeroExpediente, "duplicado"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await IngestarAsync(_ana, caso.Id, [1])).StatusCode);

        var export = await (await sofia.GetAsync($"/casos/{caso.Id}/custodia")).LeerAsync<ExportDeCustodia>();
        Assert.True(export.Verificacion.EsIntegra);
        Assert.Equal(["CasoAbierto", "AnalistaAsignado", "CasoCerrado"], export.Asientos.Select(a => a.Tipo));
    }
}
