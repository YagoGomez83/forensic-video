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
