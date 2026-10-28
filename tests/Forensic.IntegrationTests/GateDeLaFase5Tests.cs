using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Forensic.Application;
using Forensic.Application.Informes;
using Forensic.Application.Operaciones;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Ia;
using Forensic.Domain.Comun;
using Forensic.Domain.Ia;
using Forensic.Infrastructure.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Forensic.IntegrationTests;

/// <summary>
/// El gate de la Fase 5, con todo real: los workers de ingesta, de OpenCV y de IA (imágenes Docker, IA en
/// CPU), el cliente de escritorio tipado y el servidor. Un caso con material admisible y de IA da dos
/// informes separados; el bundle pericial pasa el verificador independiente y no contiene nada de IA.
/// Con FORENSIC_GATE_ARTEFACTOS=&lt;carpeta&gt;, deja ahí los dos informes y la salida del verificador.
/// </summary>
public sealed class GateDeLaFase5Tests
    : IClassFixture<ServidorForense>, IClassFixture<WorkerDeIngestaReal>, IClassFixture<WorkerDeProcesamientoReal>,
      IClassFixture<WorkerDeIaReal>, IAsyncDisposable
{
    private readonly ServidorForense _servidor;
    private readonly WorkerDeProcesamientoReal _procesamiento;
    private readonly WorkerDeIaReal _ia;
    private readonly WebApplicationFactory<Program> _real;
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "forensic-video-tests", $"gate-{Guid.NewGuid():N}")).FullName;

    public GateDeLaFase5Tests(
        ServidorForense servidor, WorkerDeIngestaReal ingesta, WorkerDeProcesamientoReal procesamiento, WorkerDeIaReal ia)
    {
        (_servidor, _procesamiento, _ia) = (servidor, procesamiento, ia);
        _real = servidor.WithWebHostBuilder(b =>
        {
            b.UseSetting("WorkerIngesta:UrlBase", ingesta.UrlBase.ToString());
            b.UseSetting("WorkerProcesamiento:UrlBase", procesamiento.UrlBase.ToString());
            b.ConfigureTestServices(s =>
            {
                s.AddHttpClient<IWorkerDeIngesta, WorkerDeIngestaHttp>((_, http) => http.BaseAddress = ingesta.UrlBase);
                s.AddHttpClient<IWorkerDeProcesamiento, WorkerDeProcesamientoHttp>((_, http) => http.BaseAddress = procesamiento.UrlBase);
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _real.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    private static JsonElement Json(byte[] bundle, string entrada)
    {
        using var zip = new ZipArchive(new MemoryStream(bundle));
        using var s = zip.GetEntry(entrada)!.Open();
        return JsonDocument.Parse(s).RootElement.Clone();
    }

    private static Dictionary<string, string> TextosSinPdf(byte[] bundle)
    {
        using var zip = new ZipArchive(new MemoryStream(bundle));
        return zip.Entries.Where(e => e.FullName != "informe.pdf")
            .ToDictionary(e => e.FullName, e => new StreamReader(e.Open(), Encoding.UTF8).ReadToEnd());
    }

    [Fact]
    public async Task Un_caso_real_da_un_pericial_verificable_sin_ia_y_un_investigativo_aparte()
    {
        var ct = CancellationToken.None;

        // El analista trabaja como lo haría desde la workstation: cliente de escritorio + worker de IA local.
        var usuario = $"gate-{Guid.NewGuid():N}"[..16];
        await _servidor.UsuarioConSesionAsync(usuario, Rol.Analista, "la frase del gate 2026");
        _real.CreateClient().Dispose();
        var cliente = ClienteForense.Crear(_real.Server.BaseAddress, new SesionEnMemoria(), _real.Server.CreateHandler());
        await cliente.IniciarSesionAsync(usuario, "la frase del gate 2026", ct);
        var http = _real.CreateClient().ComoActor(usuario, Rol.Analista);
        var orquestador = new OrquestadorDeAnalisisIa(cliente, ClienteWorkerIa.Crear(_ia.UrlBase));

        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Gate de la Fase 5", ct);
        var png = await _procesamiento.GenerarPngAsync(ancho: 320, alto: 240, semilla: 4);
        var foto = await cliente.IngestarAsync(caso.Id, "camara-entrada.png", new MemoryStream(png), png.Length, null, ct);

        // Vía admisible: un recorte con OpenCV real, reproducido byte a byte desde su asiento.
        var recorte = (await (await http.AplicarOperacionAsync(caso.Id, foto.Id, "opencv.recorte/v1",
            new { x = 100, y = 80, ancho = 48, alto = 32 }, "recorte-patente.png")).LeerAsync<Forensic.Application.Operaciones.OperacionAplicadaDto>()).Derivado;
        Assert.True((await (await http.ReproducirAsync(caso.Id, recorte.Id)).LeerAsync<Forensic.Application.Operaciones.ResultadoDeReproduccion>()).Reproducido);

        // Vía de IA (sólo investigación): detección sobre la foto, super-resolución del recorte y OCR del resultado.
        var detectado = await orquestador.DetectarAsync(caso.Id, foto.Id, await cliente.DescargarContenidoAsync(caso.Id, foto.Id, ct), 10, ct);
        var superRes = (await orquestador.SuperResolverAsync(
            caso.Id, recorte.Id, await cliente.DescargarContenidoAsync(caso.Id, recorte.Id, ct), "recorte-patente-x4.png", ct)).Derivado;
        await orquestador.LeerTextoAsync(caso.Id, superRes.Id, await cliente.DescargarContenidoAsync(caso.Id, superRes.Id, ct), 10, ct);
        Assert.NotEmpty(detectado.Hallazgos);

        // Los dos informes, cada uno por su camino.
        async Task<(InformeEmitidoDto Informe, byte[] Pdf, byte[] Bundle, string Ruta)> EmitirAsync(string tipo)
        {
            var informe = await (await http.PostAsync($"/casos/{caso.Id}/informes/{tipo}", null)).LeerAsync<InformeEmitidoDto>();
            var pdf = await (await http.GetAsync($"/casos/{caso.Id}/informes/{tipo}/{informe.Secuencia}/pdf")).Content.ReadAsByteArrayAsync();
            var bundle = await (await http.GetAsync($"/casos/{caso.Id}/informes/{tipo}/{informe.Secuencia}/bundle")).Content.ReadAsByteArrayAsync();
            var ruta = Path.Combine(_dir, $"informe-{tipo}.zip");
            await File.WriteAllBytesAsync(ruta, bundle);
            await File.WriteAllBytesAsync(Path.ChangeExtension(ruta, ".pdf"), pdf);
            return (informe, pdf, bundle, ruta);
        }
        var pericial = await EmitirAsync("pericial");
        var investigativo = await EmitirAsync("investigativo");
        var huella = (await http.ExportarAsync(caso.Id)).Sellado.ClaveDelLaboratorio;

        // 1. El verificador independiente da el pericial por íntegro, y el investigativo nunca pasa como pericial.
        var verificacionPericial = await VerificadorIndependiente.VerificarEnTextoAsync(pericial.Ruta, huella, "pericial");
        var investigativoComoPericial = await VerificadorIndependiente.VerificarEnTextoAsync(investigativo.Ruta, huella, "pericial");
        var verificacionInvestigativo = await VerificadorIndependiente.VerificarEnTextoAsync(investigativo.Ruta, huella, "investigativo");
        Assert.True(verificacionPericial.Codigo == 0, verificacionPericial.Salida);
        Assert.True(verificacionInvestigativo.Codigo == 0, verificacionInvestigativo.Salida);
        Assert.Equal(1, investigativoComoPericial.Codigo);
        Assert.Contains("RESULTADO: ÍNTEGRO", verificacionPericial.Salida);
        Assert.Contains("SÓLO INVESTIGACIÓN, NO ES PRUEBA", verificacionInvestigativo.Salida);

        // 2. Separación: el pericial sólo trae lo admisible; el investigativo, además, lo de IA.
        Guid[] Evidencias(byte[] b) => Json(b, "manifiesto.json").GetProperty("evidencias").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal([foto.Id, recorte.Id], Evidencias(pericial.Bundle));
        Assert.Equal([foto.Id, recorte.Id, superRes.Id], Evidencias(investigativo.Bundle));
        Assert.Equal("pericial", Json(pericial.Bundle, "manifiesto.json").GetProperty("tipo").GetString());
        Assert.Equal("investigativo", Json(investigativo.Bundle, "manifiesto.json").GetProperty("tipo").GetString());

        var tiposVisiblesDelPericial = Json(pericial.Bundle, "cadena.json").GetProperty("asientos").EnumerateArray()
            .Where(a => !a.TryGetProperty("opaco", out _)).Select(a => a.GetProperty("tipo").GetString()).ToHashSet();
        Assert.DoesNotContain("HallazgosRegistrados", tiposVisiblesDelPericial);
        Assert.DoesNotContain("OperacionIaAplicada", tiposVisiblesDelPericial);
        Assert.Contains("OperacionReproducida", tiposVisiblesDelPericial);

        var modelos = CatalogoDeIa.Tipos.SelectMany(CatalogoDeIa.Modelos).Select(m => m.Sha256.Hex).ToList();
        var textosPericial = TextosSinPdf(pericial.Bundle);
        foreach (var (entrada, texto) in textosPericial)
        {
            Assert.DoesNotContain(superRes.Hash, texto);
            Assert.DoesNotContain(superRes.Id.ToString(), texto);
            Assert.All(modelos, m => Assert.DoesNotContain(m, texto));
        }
        var cadenaInvestigativa = TextosSinPdf(investigativo.Bundle)["cadena.json"];
        Assert.Contains(modelos, cadenaInvestigativa.Contains);
        Assert.Contains("ia.ocr/v1", cadenaInvestigativa);

        // 3. Dos archivos distintos, cada emisión con su asiento, y la cadena del caso sigue íntegra y sellada.
        Assert.NotEqual(pericial.Informe.Bundle.Sha256, investigativo.Informe.Bundle.Sha256);
        Assert.Contains("solo-investigacion", Encoding.ASCII.GetString(investigativo.Pdf));
        Assert.DoesNotContain("solo-investigacion", Encoding.ASCII.GetString(pericial.Pdf));
        var export = await http.ExportarAsync(caso.Id);
        Assert.True(export.Verificacion.EsIntegra && export.Sellado.Verificacion.SonValidos);
        Assert.Equal(["InformePericialEmitido", "InformeInvestigativoEmitido"], export.Asientos.TakeLast(2).Select(a => a.Tipo));

        await GuardarArtefactosAsync(pericial.Pdf, pericial.Bundle, investigativo.Pdf, investigativo.Bundle, huella,
            verificacionPericial.Salida, verificacionInvestigativo.Salida, investigativoComoPericial.Salida);
    }

    private static async Task GuardarArtefactosAsync(
        byte[] pdfPericial, byte[] bundlePericial, byte[] pdfInvestigativo, byte[] bundleInvestigativo, string huella,
        string verificacionPericial, string verificacionInvestigativo, string investigativoComoPericial)
    {
        if (Environment.GetEnvironmentVariable("FORENSIC_GATE_ARTEFACTOS") is not { Length: > 0 } carpeta)
            return;
        Directory.CreateDirectory(carpeta);
        await File.WriteAllBytesAsync(Path.Combine(carpeta, "informe-pericial.pdf"), pdfPericial);
        await File.WriteAllBytesAsync(Path.Combine(carpeta, "informe-pericial.zip"), bundlePericial);
        await File.WriteAllBytesAsync(Path.Combine(carpeta, "informe-investigativo.pdf"), pdfInvestigativo);
        await File.WriteAllBytesAsync(Path.Combine(carpeta, "informe-investigativo.zip"), bundleInvestigativo);
        await File.WriteAllTextAsync(Path.Combine(carpeta, "huella.txt"), huella + "\n");
        await File.WriteAllTextAsync(Path.Combine(carpeta, "verificacion-pericial.txt"), verificacionPericial);
        await File.WriteAllTextAsync(Path.Combine(carpeta, "verificacion-investigativo.txt"), verificacionInvestigativo);
        await File.WriteAllTextAsync(Path.Combine(carpeta, "verificacion-investigativo-exigiendo-pericial.txt"), investigativoComoPericial);
    }
}
