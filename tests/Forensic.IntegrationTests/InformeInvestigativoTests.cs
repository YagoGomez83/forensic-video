using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forensic.Application.Informes;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Informes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Forensic.IntegrationTests;

/// <summary>
/// El informe investigativo (IA): aparte del pericial, por otro camino y marcado «sólo investigación».
/// Contiene lo que el pericial excluye; los dos nunca se mezclan.
/// </summary>
public sealed class InformeInvestigativoTests(ServidorForense servidor) : IClassFixture<ServidorForense>, IDisposable
{
    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "forensic-video-tests", $"investigativos-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed record Emitido(InformeEmitidoDto Informe, byte[] Pdf, byte[] Bundle, string Ruta);

    private async Task<Emitido> EmitirAsync(HttpClient cliente, Guid casoId, string tipo)
    {
        var r = await cliente.PostAsync($"/casos/{casoId}/informes/{tipo}", null);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var informe = await r.LeerAsync<InformeEmitidoDto>();
        var pdf = await (await cliente.GetAsync($"/casos/{casoId}/informes/{tipo}/{informe.Secuencia}/pdf")).Content.ReadAsByteArrayAsync();
        var bundle = await (await cliente.GetAsync($"/casos/{casoId}/informes/{tipo}/{informe.Secuencia}/bundle")).Content.ReadAsByteArrayAsync();
        var ruta = Path.Combine(_dir, $"{tipo}-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(ruta, bundle);
        return new Emitido(informe, pdf, bundle, ruta);
    }

    private static JsonElement Json(byte[] bundle, string entrada)
    {
        using var zip = new ZipArchive(new MemoryStream(bundle));
        using var s = zip.GetEntry(entrada)!.Open();
        return JsonDocument.Parse(s).RootElement.Clone();
    }

    private static string Texto(byte[] bundle, string entrada)
    {
        using var zip = new ZipArchive(new MemoryStream(bundle));
        using var s = new StreamReader(zip.GetEntry(entrada)!.Open(), Encoding.UTF8);
        return s.ReadToEnd();
    }

    private static IEnumerable<Guid> IdsDeEvidencias(byte[] bundle) =>
        Json(bundle, "manifiesto.json").GetProperty("evidencias").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());

    private async Task<string> HuellaAsync(Guid casoId) => (await _ana.ExportarAsync(casoId)).Sellado.ClaveDelLaboratorio;

    [Fact]
    public async Task Contiene_lo_que_el_pericial_excluye()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);

        var pericial = await EmitirAsync(_ana, mixto.Caso.Id, "pericial");
        var investigativo = await EmitirAsync(_ana, mixto.Caso.Id, "investigativo");

        Assert.DoesNotContain(mixto.SuperRes.Id, IdsDeEvidencias(pericial.Bundle));
        Assert.Contains(mixto.SuperRes.Id, IdsDeEvidencias(investigativo.Bundle));
        Assert.Equal("investigativo", Json(investigativo.Bundle, "manifiesto.json").GetProperty("tipo").GetString());

        var asientos = Json(investigativo.Bundle, "cadena.json").GetProperty("asientos").EnumerateArray().ToList();
        Assert.DoesNotContain(asientos, a => a.TryGetProperty("opaco", out _));
        var tipos = asientos.Select(a => a.GetProperty("tipo").GetString()).ToList();
        Assert.Contains("HallazgosRegistrados", tipos);
        Assert.Contains("OperacionIaAplicada", tipos);
        Assert.Contains(CasoMixto.RealEsrganSha256, Texto(investigativo.Bundle, "cadena.json"));
        Assert.Equal(0, investigativo.Informe.AsientosOpacos);
    }

    [Fact]
    public async Task El_bundle_investigativo_verifica_como_investigativo_y_nunca_como_pericial()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        var investigativo = await EmitirAsync(_ana, mixto.Caso.Id, "investigativo");
        var huella = await HuellaAsync(mixto.Caso.Id);

        var comoInvestigativo = await VerificadorIndependiente.VerificarAsync(investigativo.Ruta, huella, exigir: "investigativo");
        var comoPericial = await VerificadorIndependiente.VerificarAsync(investigativo.Ruta, huella, exigir: "pericial");

        Assert.True(comoInvestigativo.Codigo == 0, comoInvestigativo.Salida);
        Assert.Equal("investigativo", comoInvestigativo.Resultado.GetProperty("tipo").GetString());
        Assert.Equal(1, comoPericial.Codigo);
    }

    [Fact]
    public async Task Esta_marcado_solo_investigacion_en_el_pdf_y_en_el_bundle()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);

        var investigativo = await EmitirAsync(_ana, mixto.Caso.Id, "investigativo");
        var pericial = await EmitirAsync(_ana, mixto.Caso.Id, "pericial");

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(investigativo.Pdf, 0, 5));
        Assert.Contains("solo-investigacion", Encoding.ASCII.GetString(investigativo.Pdf));
        Assert.DoesNotContain("solo-investigacion", Encoding.ASCII.GetString(pericial.Pdf));
        Assert.Contains("SÓLO INVESTIGACIÓN, NO ES PRUEBA", Texto(investigativo.Bundle, "LEEME.txt"));
        Assert.Equal("investigativo", investigativo.Informe.Tipo);
    }

    [Fact]
    public async Task El_pdf_describe_los_hallazgos_como_lo_que_dijo_el_modelo()
    {
        var redactados = new List<InformeInvestigativoParaRedactar>();
        using var conRegistro = servidor.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IRedactorDeInformes>(new RedactorQueRegistra(new RedactorDePdf(), redactados))));
        var ana = conRegistro.CreateClient().ComoActor("ana", Rol.Analista);
        var mixto = await CasoMixto.CrearAsync(ana);

        (await ana.PostAsync($"/casos/{mixto.Caso.Id}/informes/investigativo", null)).EnsureSuccessStatusCode();

        var analisis = Assert.Single(Assert.Single(redactados).Analisis);
        Assert.Equal(mixto.Original.Id, analisis.EvidenciaId);
        Assert.Equal(["el modelo detectó «car» con 0,90 en x=1, y=1, 2×2 px"], analisis.Hallazgos);
        Assert.Contains(Assert.Single(redactados).Evidencias, e => e.Id == mixto.SuperRes.Id && e.SoloInvestigacion);
    }

    [Fact]
    public async Task Cada_informe_se_descarga_solo_por_su_propia_ruta()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        var investigativo = await EmitirAsync(_ana, mixto.Caso.Id, "investigativo");
        var pericial = await EmitirAsync(_ana, mixto.Caso.Id, "pericial");

        foreach (var archivo in new[] { "pdf", "bundle" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _ana.GetAsync(
                $"/casos/{mixto.Caso.Id}/informes/pericial/{investigativo.Informe.Secuencia}/{archivo}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _ana.GetAsync(
                $"/casos/{mixto.Caso.Id}/informes/investigativo/{pericial.Informe.Secuencia}/{archivo}")).StatusCode);
        }
        var lista = await (await _ana.GetAsync($"/casos/{mixto.Caso.Id}/informes")).LeerAsync<List<InformeEmitidoDto>>();
        Assert.Equal(["investigativo", "pericial"], lista.Select(i => i.Tipo));
    }

    [Fact]
    public async Task Un_pericial_posterior_deja_opaca_la_emision_del_investigativo()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);
        var investigativo = await EmitirAsync(_ana, mixto.Caso.Id, "investigativo");

        var pericial = await EmitirAsync(_ana, mixto.Caso.Id, "pericial");

        var emision = Json(pericial.Bundle, "cadena.json").GetProperty("asientos").EnumerateArray()
            .Single(a => a.GetProperty("secuencia").GetInt64() == investigativo.Informe.Secuencia);
        Assert.True(emision.GetProperty("opaco").GetBoolean());
        foreach (var entrada in new[] { "manifiesto.json", "cadena.json", "sellos.json", "LEEME.txt" })
            Assert.DoesNotContain(investigativo.Informe.Bundle.Sha256, Texto(pericial.Bundle, entrada));
        Assert.Equal(0, (await VerificadorIndependiente.VerificarAsync(pericial.Ruta, await HuellaAsync(mixto.Caso.Id), exigir: "pericial")).Codigo);
    }

    [Fact]
    public async Task La_emision_del_investigativo_se_asienta_aparte()
    {
        var mixto = await CasoMixto.CrearAsync(_ana);

        var investigativo = await EmitirAsync(_ana, mixto.Caso.Id, "investigativo");

        var asiento = (await _ana.ExportarAsync(mixto.Caso.Id)).Asientos[^1];
        Assert.Equal("InformeInvestigativoEmitido", asiento.Tipo);
        Assert.Equal("ana", asiento.Actor);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(investigativo.Pdf)).ToLowerInvariant(), asiento.Detalle);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(investigativo.Bundle)).ToLowerInvariant(), asiento.Detalle);
    }

    [Fact]
    public async Task Un_auditor_no_emite_el_investigativo()
    {
        var caso = await _ana.AbrirCasoAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await servidor.ClienteDe("aurelio", Rol.Auditor).PostAsync($"/casos/{caso.Id}/informes/investigativo", null)).StatusCode);
    }

    private sealed class RedactorQueRegistra(IRedactorDeInformes real, List<InformeInvestigativoParaRedactar> registro) : IRedactorDeInformes
    {
        public byte[] RedactarPericial(InformePericialParaRedactar informe) => real.RedactarPericial(informe);

        public byte[] RedactarInvestigativo(InformeInvestigativoParaRedactar informe)
        {
            registro.Add(informe);
            return real.RedactarInvestigativo(informe);
        }
    }
}
