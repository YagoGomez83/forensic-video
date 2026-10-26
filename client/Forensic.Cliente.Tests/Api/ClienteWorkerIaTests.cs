using System.Net;
using System.Text;
using Forensic.Cliente.Api;
using Forensic.Cliente.Tests.Dobles;

namespace Forensic.Cliente.Tests.Api;

public class ClienteWorkerIaTests
{
    private const string Respuesta = """{"analisis": "ia.deteccion/v1", "hallazgos": [{"confianza": 0.8213145732879639}]}""";

    private readonly ServidorFalso _worker = new();

    private ClienteWorkerIa Worker() => ClienteWorkerIa.Crear(new Uri("http://127.0.0.1:8100/"), _worker);

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Detectar_manda_la_imagen_en_crudo_con_el_umbral_y_devuelve_la_respuesta_tal_cual()
    {
        _worker.En(HttpMethod.Post, "/v1/detectar", _ => Json(HttpStatusCode.OK, Respuesta));
        byte[] imagen = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

        var reporte = await Worker().DetectarAsync(imagen, 500, CancellationToken.None);

        Assert.Equal(Respuesta, reporte);
        var cuerpo = _worker.Cuerpos.Single()!;
        Assert.Contains("name=parametros", cuerpo);
        Assert.Contains("""{"umbralConfianzaMilesimas":500}""", cuerpo);
        Assert.Contains("name=entrada", cuerpo);
        Assert.True(ContieneSecuencia(_worker.Bytes.Single()!, imagen));
    }

    [Theory]
    [InlineData("http://192.168.0.10:8100/")]
    [InlineData("http://worker-ia:8100/")]
    [InlineData("https://ia.ejemplo.org/")]
    public void El_worker_de_ia_solo_puede_estar_en_la_misma_workstation(string url)
    {
        Assert.Throws<ArgumentException>(() => ClienteWorkerIa.Crear(new Uri(url)));
    }

    [Fact]
    public async Task Sin_worker_en_marcha_el_error_lo_dice()
    {
        _worker.Inalcanzable = true;

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Worker().DetectarAsync([1], 500, CancellationToken.None));

        Assert.Equal(TipoDeError.WorkerNoDisponible, ex.Tipo);
        Assert.Contains("worker de IA", ex.Message);
        Assert.Contains("127.0.0.1:8100", ex.Message);
    }

    [Fact]
    public async Task Un_rechazo_del_worker_muestra_su_detalle()
    {
        _worker.En(HttpMethod.Post, "/v1/detectar",
            _ => Json(HttpStatusCode.UnprocessableEntity, """{"detail": "El contenido no es una imagen que OpenCV pueda leer."}"""));

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Worker().DetectarAsync([1], 500, CancellationToken.None));

        Assert.Equal(TipoDeError.Rechazado, ex.Tipo);
        Assert.Equal("El contenido no es una imagen que OpenCV pueda leer.", ex.Message);
    }

    [Fact]
    public async Task Otro_error_del_worker_no_se_disfraza_de_rechazo()
    {
        _worker.En(HttpMethod.Post, "/v1/detectar", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Worker().DetectarAsync([1], 500, CancellationToken.None));

        Assert.Equal(TipoDeError.WorkerNoDisponible, ex.Tipo);
        Assert.Contains("500", ex.Message);
    }

    private static bool ContieneSecuencia(byte[] datos, byte[] buscada) =>
        Enumerable.Range(0, datos.Length - buscada.Length + 1).Any(i => datos.AsSpan(i, buscada.Length).SequenceEqual(buscada));
}
