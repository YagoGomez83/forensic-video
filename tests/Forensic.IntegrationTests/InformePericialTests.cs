using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forensic.Application.Informes;
using Forensic.Domain.Comun;
using Microsoft.AspNetCore.Hosting;

namespace Forensic.IntegrationTests;

/// <summary>
/// El informe pericial: sólo lo admisible en juicio (por construcción), PDF + bundle verificable, y
/// su emisión asentada en la cadena.
/// </summary>
public sealed class InformePericialTests(ServidorForense servidor) : IClassFixture<ServidorForense>, IDisposable
{
    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);
    private readonly HttpClient _sofia = servidor.ClienteDe("sofia", Rol.Supervisor);
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "forensic-video-tests", $"informes-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Task<HttpResponseMessage> EmitirAsync(HttpClient cliente, Guid casoId) =>
        cliente.PostAsync($"/casos/{casoId}/informes/pericial", null);

    private async Task<(InformeEmitidoDto Informe, byte[] Pdf, byte[] Bundle, string RutaBundle)> EmitirYDescargarAsync(
        Guid casoId, HttpClient? cliente = null)
    {
        var r = await EmitirAsync(cliente ?? _ana, casoId);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var informe = await r.LeerAsync<InformeEmitidoDto>();
        var pdf = await DescargarAsync($"/casos/{casoId}/informes/pericial/{informe.Secuencia}/pdf", "application/pdf");
        var bundle = await DescargarAsync($"/casos/{casoId}/informes/pericial/{informe.Secuencia}/bundle", "application/zip");
        var ruta = Path.Combine(_dir, $"pericial-{informe.Secuencia}-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(ruta, bundle);
        return (informe, pdf, bundle, ruta);
    }

    private async Task<byte[]> DescargarAsync(string url, string tipo)
    {
        var r = await servidor.ClienteDe("aurelio", Rol.Auditor).GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(tipo, r.Content.Headers.ContentType?.MediaType);
        return await r.Content.ReadAsByteArrayAsync();
    }

    private async Task<string> HuellaAsync(Guid casoId) => (await _ana.ExportarAsync(casoId)).Sellado.ClaveDelLaboratorio;

    private static JsonElement Json(ZipArchive zip, string entrada)
    {
        using var s = zip.GetEntry(entrada)!.Open();
        return JsonDocument.Parse(s).RootElement.Clone();
    }

    private static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public async Task Un_caso_con_ia_da_un_pericial_que_no_la_incluye()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);

        var (informe, _, bundle, _) = await EmitirYDescargarAsync(mixto.Caso.Id);

        using var zip = new ZipArchive(new MemoryStream(bundle));
        var manifiesto = Json(zip, "manifiesto.json");
        Assert.Equal("pericial", manifiesto.GetProperty("tipo").GetString());
        Assert.Equal([mixto.Original.Id, mixto.Recorte.Id],
            manifiesto.GetProperty("evidencias").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal(1, informe.EvidenciasExcluidas);

        var cadena = Json(zip, "cadena.json").GetProperty("asientos").EnumerateArray().ToList();
        var tipos = cadena.Where(a => a.TryGetProperty("tipo", out _)).Select(a => a.GetProperty("tipo").GetString()).ToList();
        Assert.DoesNotContain("HallazgosRegistrados", tipos);
        Assert.DoesNotContain("OperacionIaAplicada", tipos);
        // Detección, super-resolución y el acceso al derivado de IA: opacos.
        Assert.Equal(3, cadena.Count(a => a.TryGetProperty("opaco", out _)));
        Assert.Equal(3, informe.AsientosOpacos);

        // Nada de la IA en ningún archivo del bundle salvo el PDF (que se revisa aparte).
        foreach (var e in zip.Entries.Where(e => e.FullName != "informe.pdf"))
        {
            using var lector = new StreamReader(e.Open(), Encoding.UTF8);
            var texto = await lector.ReadToEndAsync();
            Assert.DoesNotContain(mixto.SuperRes.Hash, texto);
            Assert.DoesNotContain(mixto.SuperRes.Id.ToString(), texto);
            Assert.DoesNotContain(CasoMixto.RealEsrganSha256, texto);
            Assert.DoesNotContain(CasoMixto.YoloxSha256, texto);
        }
    }

    [Fact]
    public async Task El_bundle_pericial_pasa_el_verificador_independiente()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        var (_, _, _, ruta) = await EmitirYDescargarAsync(mixto.Caso.Id);

        var corrida = await VerificadorIndependiente.VerificarAsync(ruta, await HuellaAsync(mixto.Caso.Id), exigir: "pericial");

        Assert.True(corrida.Codigo == 0, corrida.Salida);
        Assert.Equal("ÍNTEGRO", corrida.Estado);
        Assert.Equal(3, corrida.Resultado.GetProperty("asientosOpacos").GetInt32());
    }

    [Fact]
    public async Task Un_bundle_pericial_manipulado_no_pasa_el_verificador()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        var (_, _, bundle, _) = await EmitirYDescargarAsync(mixto.Caso.Id);

        var ruta = Path.Combine(_dir, "manipulado.zip");
        await File.WriteAllBytesAsync(ruta, bundle);
        using (var zip = ZipFile.Open(ruta, ZipArchiveMode.Update))
        {
            var entrada = zip.Entries.Single(e => e.FullName.StartsWith("evidencias/") && e.FullName.Contains(mixto.Recorte.Id.ToString()));
            var nombre = entrada.FullName;
            entrada.Delete();
            using var w = zip.CreateEntry(nombre).Open();
            w.Write("un recorte retocado"u8);
        }

        var corrida = await VerificadorIndependiente.VerificarAsync(ruta, await HuellaAsync(mixto.Caso.Id));

        Assert.Equal(1, corrida.Codigo);
        Assert.Contains(corrida.Fallas, f => f.Contains(mixto.Recorte.Id.ToString()) || f.Contains("evidencias/"));
    }

    [Fact]
    public async Task La_emision_se_asienta_con_quien_cuando_y_los_hashes_del_pdf_y_del_bundle()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);

        var (informe, pdf, bundle, _) = await EmitirYDescargarAsync(mixto.Caso.Id);

        var export = await _ana.ExportarAsync(mixto.Caso.Id);
        Assert.True(export.Verificacion.EsIntegra);
        var asiento = export.Asientos[^1];
        Assert.Equal("InformePericialEmitido", asiento.Tipo);
        Assert.Equal(informe.Secuencia, asiento.Secuencia);
        Assert.Equal("ana", asiento.Actor);
        Assert.Equal(informe.EmitidoEn, asiento.Timestamp);
        Assert.Null(asiento.EvidenciaId);
        Assert.Contains(Hex(pdf), asiento.Detalle);
        Assert.Contains(Hex(bundle), asiento.Detalle);
        Assert.Equal((Hex(pdf), Hex(bundle)), (informe.Pdf.Sha256, informe.Bundle.Sha256));

        // El bundle cubre la cadena hasta el asiento anterior a su propia emisión, y trae el mismo PDF.
        using var zip = new ZipArchive(new MemoryStream(bundle));
        Assert.Equal(informe.Secuencia - 1, Json(zip, "manifiesto.json").GetProperty("cadena").GetProperty("hasta").GetInt64());
        using var pdfDelBundle = new MemoryStream();
        await zip.GetEntry("informe.pdf")!.Open().CopyToAsync(pdfDelBundle);
        Assert.Equal(pdf, pdfDelBundle.ToArray());
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public async Task Antes_de_emitir_se_sella_la_cabeza_que_cubre_el_bundle()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);

        var (informe, _, _, _) = await EmitirYDescargarAsync(mixto.Caso.Id);

        var export = await _ana.ExportarAsync(mixto.Caso.Id);
        Assert.Contains(export.Sellado.Sellos, s => s.Secuencia == informe.Hasta);
        Assert.True(export.Sellado.Verificacion.SonValidos);
    }

    [Fact]
    public async Task Un_segundo_informe_incluye_completa_la_emision_del_primero_y_tambien_verifica()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        var (primero, _, _, _) = await EmitirYDescargarAsync(mixto.Caso.Id);

        var (_, _, bundle, ruta) = await EmitirYDescargarAsync(mixto.Caso.Id);

        using var zip = new ZipArchive(new MemoryStream(bundle));
        var emision = Json(zip, "cadena.json").GetProperty("asientos").EnumerateArray()
            .Single(a => a.GetProperty("secuencia").GetInt64() == primero.Secuencia);
        Assert.Equal("InformePericialEmitido", emision.GetProperty("tipo").GetString());
        Assert.Equal(0, (await VerificadorIndependiente.VerificarAsync(ruta, await HuellaAsync(mixto.Caso.Id), exigir: "pericial")).Codigo);
        var lista = await (await _ana.GetAsync($"/casos/{mixto.Caso.Id}/informes")).LeerAsync<List<InformeEmitidoDto>>();
        Assert.Equal(2, lista.Count);
        Assert.All(lista, i => Assert.Equal("pericial", i.Tipo));
    }

    [Fact]
    public async Task Por_encima_del_umbral_la_evidencia_viaja_aparte_y_el_verificador_la_busca_por_hash()
    {
        using var conUmbralBajo = servidor.WithWebHostBuilder(b => b.UseSetting("Informes:UmbralDeArchivoEmbebidoBytes", "1000"));
        var ana = conUmbralBajo.CreateClient().ComoActor("ana", Rol.Analista);
        var mixto = await CasoMixto.CrearAsync(ana);
        var informe = await (await EmitirAsync(ana, mixto.Caso.Id)).LeerAsync<InformeEmitidoDto>();
        var ruta = Path.Combine(_dir, "con-externo.zip");
        await File.WriteAllBytesAsync(ruta,
            await (await ana.GetAsync($"/casos/{mixto.Caso.Id}/informes/pericial/{informe.Secuencia}/bundle")).Content.ReadAsByteArrayAsync());
        var huella = (await ana.ExportarAsync(mixto.Caso.Id)).Sellado.ClaveDelLaboratorio;

        using (var zip = ZipFile.OpenRead(ruta))
        {
            var original = Json(zip, "manifiesto.json").GetProperty("evidencias").EnumerateArray()
                .Single(e => e.GetProperty("id").GetGuid() == mixto.Original.Id);
            Assert.False(original.GetProperty("embebida").GetBoolean());
            Assert.Equal(JsonValueKind.Null, original.GetProperty("ruta").ValueKind);
        }
        Assert.Equal(2, (await VerificadorIndependiente.VerificarAsync(ruta, huella)).Codigo);

        var externos = Directory.CreateDirectory(Path.Combine(_dir, "externos")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(externos, "copia del original"),
            await (await ana.GetAsync($"/casos/{mixto.Caso.Id}/evidencias/{mixto.Original.Id}/contenido")).Content.ReadAsByteArrayAsync());
        Assert.Equal(0, (await VerificadorIndependiente.VerificarAsync(ruta, huella, externos)).Codigo);
    }

    [Fact]
    public async Task Ni_un_auditor_ni_un_analista_ajeno_emiten()
    {
        var caso = await _ana.AbrirCasoAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await EmitirAsync(servidor.ClienteDe("aurelio", Rol.Auditor), caso.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EmitirAsync(servidor.ClienteDe("beto", Rol.Analista), caso.Id)).StatusCode);
    }

    [Fact]
    public async Task Con_el_caso_cerrado_emite_el_supervisor_y_no_el_analista()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        (await _sofia.PostAsync($"/casos/{mixto.Caso.Id}/cerrar", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await EmitirAsync(_ana, mixto.Caso.Id)).StatusCode);
        var (informe, _, _, ruta) = await EmitirYDescargarAsync(mixto.Caso.Id, _sofia);

        Assert.Equal("sofia", informe.EmitidoPor);
        Assert.Equal(0, (await VerificadorIndependiente.VerificarAsync(ruta, await HuellaAsync(mixto.Caso.Id), exigir: "pericial")).Codigo);
    }

    [Fact]
    public async Task Con_la_cadena_reconstruida_no_se_emite_ni_se_asienta_nada()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        await servidor.SellarPendientesAsync();
        await servidor.ReconstruirCadenaAsync(mixto.Caso.Id);
        var antes = (await _ana.ExportarAsync(mixto.Caso.Id)).Asientos.Count;

        Assert.Equal(HttpStatusCode.Locked, (await EmitirAsync(_ana, mixto.Caso.Id)).StatusCode);

        Assert.Equal(antes, (await _ana.ExportarAsync(mixto.Caso.Id)).Asientos.Count);
    }

    [Fact]
    public async Task El_pericial_no_se_descarga_por_la_ruta_de_otro_asiento()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        await EmitirYDescargarAsync(mixto.Caso.Id);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _ana.GetAsync($"/casos/{mixto.Caso.Id}/informes/pericial/2/bundle")).StatusCode);
    }
}
