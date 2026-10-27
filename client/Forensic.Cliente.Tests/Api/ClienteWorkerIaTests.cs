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

    [Fact]
    public async Task Leer_texto_manda_la_imagen_en_crudo_con_el_umbral_y_devuelve_la_respuesta_tal_cual()
    {
        const string ocr = """{"analisis": "ia.ocr/v1", "hallazgos": [{"texto": "ABC 123", "confianza": 0.9113}]}""";
        _worker.En(HttpMethod.Post, "/v1/ocr", _ => Json(HttpStatusCode.OK, ocr));
        byte[] imagen = [0x89, 0x50, 0x4E, 0x47, 4, 5, 6];

        var reporte = await Worker().LeerTextoAsync(imagen, 700, CancellationToken.None);

        Assert.Equal(ocr, reporte);
        var cuerpo = _worker.Cuerpos.Single()!;
        Assert.Contains("""{"umbralConfianzaMilesimas":700}""", cuerpo);
        Assert.True(ContieneSecuencia(_worker.Bytes.Single()!, imagen));
    }

    [Fact]
    public async Task Super_resolver_devuelve_el_png_y_el_reporte_de_la_cabecera_tal_cual()
    {
        const string cabecera = """{"operacion":"ia.superresolucion/v1","parametros":{},"salida":{"sha256":"ab"}}""";
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9];
        _worker.En(HttpMethod.Post, "/v1/superresolucion", _ =>
        {
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            respuesta.Headers.Add("X-Reporte", cabecera);
            return respuesta;
        });
        byte[] imagen = [1, 2, 3];

        var (derivado, reporte) = await Worker().SuperResolverAsync(imagen, CancellationToken.None);

        Assert.Equal(png, derivado);
        Assert.Equal(cabecera, reporte);
        Assert.Contains("name=parametros", _worker.Cuerpos.Single()!);
        Assert.Contains("{}", _worker.Cuerpos.Single()!);
        Assert.True(ContieneSecuencia(_worker.Bytes.Single()!, imagen));
    }

    [Fact]
    public async Task Un_derivado_sin_reporte_no_tiene_procedencia_y_no_se_acepta()
    {
        _worker.En(HttpMethod.Post, "/v1/superresolucion",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x89, 0x50]) });

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Worker().SuperResolverAsync([1], CancellationToken.None));

        Assert.Equal(TipoDeError.WorkerNoDisponible, ex.Tipo);
        Assert.Contains("X-Reporte", ex.Message);
    }

    [Fact]
    public async Task Un_rechazo_de_la_super_resolucion_muestra_el_detalle_del_worker()
    {
        _worker.En(HttpMethod.Post, "/v1/superresolucion",
            _ => Json(HttpStatusCode.UnprocessableEntity, """{"detail": "La imagen es multipágina."}"""));

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Worker().SuperResolverAsync([1], CancellationToken.None));

        Assert.Equal((TipoDeError.Rechazado, "La imagen es multipágina."), (ex.Tipo, ex.Message));
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
