using System.Net;
using System.Text;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Ia;
using Forensic.Cliente.Tests.Dobles;

namespace Forensic.Cliente.Tests.Ia;

public class OrquestadorDeAnalisisIaTests
{
    private const string ReporteDelWorker = """{"analisis": "ia.deteccion/v1", "hallazgos": []}""";

    private readonly ServidorFalso _servidor = new();
    private readonly ServidorFalso _worker = new();
    private readonly Guid _casoId = Guid.NewGuid();
    private readonly Guid _evidenciaId = Guid.NewGuid();

    private OrquestadorDeAnalisisIa Orquestador() => new(
        ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor),
        ClienteWorkerIa.Crear(new Uri("http://127.0.0.1:8100/"), _worker));

    private string RutaDelServidor => $"/casos/{_casoId}/evidencias/{_evidenciaId}/analisis-ia";

    private static object AnalisisRegistrado(Guid evidenciaId) => new
    {
        id = Guid.NewGuid(), evidenciaId, analisis = "ia.deteccion/v1", parametros = "{}",
        modelo = new { id = "yolox-s", version = "0.1.1rc0", sha256 = new string('c', 64) }, entorno = "{}", actor = "lucia",
        registradoEn = DateTimeOffset.UtcNow, via = "SoloInvestigacion", hallazgos = Array.Empty<object>(),
    };

    [Fact]
    public async Task Corre_el_worker_local_sobre_la_evidencia_y_reporta_su_respuesta_tal_cual_al_servidor()
    {
        _worker.En(HttpMethod.Post, "/v1/detectar",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReporteDelWorker, Encoding.UTF8, "application/json") });
        _servidor.En(HttpMethod.Post, RutaDelServidor, HttpStatusCode.Created, AnalisisRegistrado(_evidenciaId));
        byte[] contenido = [1, 2, 3, 4];

        var analisis = await Orquestador().DetectarAsync(_casoId, _evidenciaId, contenido, 250, CancellationToken.None);

        Assert.Equal(_evidenciaId, analisis.EvidenciaId);
        Assert.Contains("""{"umbralConfianzaMilesimas":250}""", _worker.Cuerpos.Single());
        Assert.Equal(ReporteDelWorker, _servidor.Cuerpos.Single());
    }

    [Fact]
    public async Task El_ocr_del_worker_local_se_reporta_tal_cual_como_analisis()
    {
        const string ocr = """{"analisis": "ia.ocr/v1", "hallazgos": []}""";
        _worker.En(HttpMethod.Post, "/v1/ocr",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ocr, Encoding.UTF8, "application/json") });
        _servidor.En(HttpMethod.Post, RutaDelServidor, HttpStatusCode.Created, AnalisisRegistrado(_evidenciaId));

        var analisis = await Orquestador().LeerTextoAsync(_casoId, _evidenciaId, [1, 2], 600, CancellationToken.None);

        Assert.Equal(_evidenciaId, analisis.EvidenciaId);
        Assert.Contains("""{"umbralConfianzaMilesimas":600}""", _worker.Cuerpos.Single());
        Assert.Equal(ocr, _servidor.Cuerpos.Single());
    }

    [Fact]
    public async Task La_super_resolucion_local_se_registra_en_el_servidor_con_su_png_y_su_reporte()
    {
        const string reporte = """{"operacion":"ia.superresolucion/v1","parametros":{}}""";
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 3];
        _worker.En(HttpMethod.Post, "/v1/superresolucion", _ =>
        {
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            respuesta.Headers.Add("X-Reporte", reporte);
            return respuesta;
        });
        var derivadoId = Guid.NewGuid();
        _servidor.En(HttpMethod.Post, $"/casos/{_casoId}/evidencias/{_evidenciaId}/operaciones-ia", HttpStatusCode.Created, new
        {
            derivado = new
            {
                id = derivadoId, casoId = _casoId, hash = new string('d', 64), tamanoBytes = png.Length, nombreOriginal = "cam1-x4.png",
                via = "SoloInvestigacion", padreId = _evidenciaId, ingestadaEn = DateTimeOffset.UtcNow, ingestadaPor = "lucia",
                controlCruzadoPendiente = false,
            },
            operacionId = Guid.NewGuid(), tipo = "ia.superresolucion/v1", parametros = "{}", entorno = "{}",
        });

        var aplicada = await Orquestador().SuperResolverAsync(_casoId, _evidenciaId, [1, 2], "cam1-x4.png", CancellationToken.None);

        Assert.Equal((derivadoId, ViaProcesamiento.SoloInvestigacion), (aplicada.Derivado.Id, aplicada.Derivado.Via));
        Assert.Contains(reporte, _servidor.Cuerpos.Single());
        Assert.Contains("cam1-x4.png", _servidor.Cuerpos.Single());
    }

    [Fact]
    public async Task Si_la_super_resolucion_falla_no_se_registra_nada_en_el_servidor()
    {
        _worker.Inalcanzable = true;

        await Assert.ThrowsAsync<ErrorDeApi>(
            () => Orquestador().SuperResolverAsync(_casoId, _evidenciaId, [1], null, CancellationToken.None));

        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task Si_el_worker_falla_no_se_reporta_nada_al_servidor()
    {
        _worker.Inalcanzable = true;

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(
            () => Orquestador().DetectarAsync(_casoId, _evidenciaId, [1], 500, CancellationToken.None));

        Assert.Equal(TipoDeError.WorkerNoDisponible, ex.Tipo);
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task Si_el_servidor_rechaza_el_reporte_el_error_es_el_del_servidor()
    {
        _worker.En(HttpMethod.Post, "/v1/detectar",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReporteDelWorker, Encoding.UTF8, "application/json") });
        _servidor.En(HttpMethod.Post, RutaDelServidor,
            _ => ServidorFalso.Problema(HttpStatusCode.UnprocessableEntity, "Regla forense violada", "El worker analizó otro contenido."));

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(
            () => Orquestador().DetectarAsync(_casoId, _evidenciaId, [1], 500, CancellationToken.None));

        Assert.Equal((TipoDeError.Rechazado, "El worker analizó otro contenido."), (ex.Tipo, ex.Message));
    }
}
