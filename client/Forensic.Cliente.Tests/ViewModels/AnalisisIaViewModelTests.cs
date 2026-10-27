using System.Net;
using System.Text;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Ia;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.ViewModels;

public class AnalisisIaViewModelTests
{
    private readonly ServidorFalso _servidor = new();
    private readonly ServidorFalso _worker = new();
    private readonly NavegacionRegistrada _navegacion = new();
    private readonly ClienteForense _api;

    public AnalisisIaViewModelTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    private static string RutaDeAnalisis => $"/casos/{CasoId}/evidencias/{EvidenciaId}/analisis-ia";

    private async Task<VisorViewModel> VisorConContenidoAsync(byte[]? contenido)
    {
        var visor = new VisorViewModel(_api, _navegacion, volverA: null, await VisorViewModelTests.EvidenciaDtoAsync()) { Contenido = contenido };
        return visor;
    }

    private async Task<AnalisisIaViewModel> PantallaAsync(byte[]? contenido = null) => new(
        _api, new OrquestadorDeAnalisisIa(_api, ClienteWorkerIa.Crear(new Uri("http://127.0.0.1:8100/"), _worker)),
        _navegacion, await VisorConContenidoAsync(contenido ?? [1, 2, 3]));

    internal static object Analisis(
        string tipo = "ia.deteccion/v1", string? clase = "car", string? texto = null, decimal confianza = 0.8213145732879639m,
        string modelo = "yolox-s", DateTimeOffset? en = null) => new
    {
        id = Guid.NewGuid(), evidenciaId = EvidenciaId, analisis = tipo, parametros = """{"umbralConfianzaMilesimas":500}""",
        modelo = new { id = modelo, version = "v1", sha256 = new string('c', 64) }, entorno = "{}", actor = "lucia",
        registradoEn = en ?? DateTimeOffset.UtcNow, via = "SoloInvestigacion",
        hallazgos = new[]
        {
            new
            {
                orden = 0, region = new { x = 10, y = 20, ancho = 30, alto = 40 }, clase, textoLeido = texto, confianza,
                crudo = "{}", via = "SoloInvestigacion",
            },
        },
    };

    private static HttpResponseMessage Json(string cuerpo) =>
        new(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Al_cargar_lista_los_analisis_registrados_y_muestra_el_mas_reciente()
    {
        var antes = DateTimeOffset.UtcNow.AddHours(-1);
        _servidor.En(HttpMethod.Get, RutaDeAnalisis, HttpStatusCode.OK, new[]
        {
            Analisis(en: antes),
            Analisis("ia.ocr/v1", clase: null, texto: "ABC123", confianza: 0.82m, modelo: "ppocrv4-en"),
        });
        var pantalla = await PantallaAsync();

        await pantalla.CargarCommand.ExecuteAsync(null);

        Assert.Equal(2, pantalla.Analisis.Count);
        Assert.Equal("ia.ocr/v1", pantalla.AnalisisElegido!.Analisis);
        var hallazgo = Assert.Single(pantalla.Hallazgos);
        Assert.Equal("el modelo leyó «ABC123» con 0,82", hallazgo.Descripcion);
        Assert.Equal(new Region(10, 20, 30, 40), hallazgo.Region);
    }

    [Fact]
    public async Task Una_deteccion_dice_lo_que_el_modelo_detecto_no_lo_que_hay()
    {
        _servidor.En(HttpMethod.Get, RutaDeAnalisis, HttpStatusCode.OK, new[] { Analisis() });
        var pantalla = await PantallaAsync();

        await pantalla.CargarCommand.ExecuteAsync(null);

        var hallazgo = Assert.Single(pantalla.Hallazgos);
        Assert.Equal("el modelo detectó «car» con 0,82", hallazgo.Descripcion);
        Assert.Equal("car 0,82", hallazgo.Etiqueta);
    }

    [Fact]
    public async Task Detectar_corre_el_worker_local_con_el_umbral_en_milesimas_y_muestra_el_analisis_registrado()
    {
        const string reporte = """{"analisis": "ia.deteccion/v1"}""";
        _worker.En(HttpMethod.Post, "/v1/detectar", _ => Json(reporte));
        _servidor.En(HttpMethod.Post, RutaDeAnalisis, HttpStatusCode.Created, Analisis(clase: "truck"));
        byte[] contenido = [7, 7, 7];
        var pantalla = await PantallaAsync(contenido);
        pantalla.Umbral = 0.25m;

        await pantalla.DetectarCommand.ExecuteAsync(null);

        Assert.Null(pantalla.Error);
        Assert.Contains("""{"umbralConfianzaMilesimas":250}""", _worker.Cuerpos.Single());
        Assert.Equal(reporte, _servidor.Cuerpos.Single());
        Assert.Equal("el modelo detectó «truck» con 0,82", Assert.Single(pantalla.Hallazgos).Descripcion);
        Assert.Same(pantalla.AnalisisElegido, pantalla.Analisis[^1]);
    }

    [Fact]
    public async Task Leer_texto_corre_el_ocr_local_y_muestra_lo_que_leyo_el_modelo()
    {
        _worker.En(HttpMethod.Post, "/v1/ocr", _ => Json("""{"analisis": "ia.ocr/v1"}"""));
        _servidor.En(HttpMethod.Post, RutaDeAnalisis, HttpStatusCode.Created,
            Analisis("ia.ocr/v1", clase: null, texto: "AB 123 CD", confianza: 0.9m, modelo: "ppocrv4-en"));
        var pantalla = await PantallaAsync();

        await pantalla.LeerTextoCommand.ExecuteAsync(null);

        Assert.Contains("""{"umbralConfianzaMilesimas":500}""", _worker.Cuerpos.Single());
        Assert.Equal("el modelo leyó «AB 123 CD» con 0,90", Assert.Single(pantalla.Hallazgos).Descripcion);
    }

    [Theory]
    [InlineData("0.2505", "admite hasta 3 decimales")]
    [InlineData(null, "Falta el umbral")]
    public async Task Un_umbral_que_no_se_puede_expresar_en_milesimas_no_llega_al_worker(string? umbral, string mensaje)
    {
        var pantalla = await PantallaAsync();
        pantalla.Umbral = umbral is null ? null : decimal.Parse(umbral, System.Globalization.CultureInfo.InvariantCulture);

        await pantalla.DetectarCommand.ExecuteAsync(null);

        Assert.Contains(mensaje, pantalla.Error);
        Assert.Empty(_worker.Peticiones);
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task Sin_la_imagen_en_memoria_no_hay_analisis()
    {
        var visor = await VisorConContenidoAsync(null);
        var pantalla = new AnalisisIaViewModel(
            _api, new OrquestadorDeAnalisisIa(_api, ClienteWorkerIa.Crear(new Uri("http://127.0.0.1:8100/"), _worker)), _navegacion, visor);

        await pantalla.DetectarCommand.ExecuteAsync(null);

        Assert.NotNull(pantalla.Error);
        Assert.Empty(_worker.Peticiones);
    }

    [Fact]
    public async Task Con_el_worker_caido_el_error_dice_como_levantarlo_y_no_se_reporta_nada()
    {
        _worker.Inalcanzable = true;
        var pantalla = await PantallaAsync();

        await pantalla.LeerTextoCommand.ExecuteAsync(null);

        Assert.Contains("worker-ia", pantalla.Error);
        Assert.Empty(_servidor.Peticiones);
        Assert.False(pantalla.Ocupado);
    }

    [Fact]
    public async Task La_super_resolucion_abre_el_derivado_en_el_visor_con_su_modelo_como_origen()
    {
        _worker.En(HttpMethod.Post, "/v1/superresolucion", _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]) };
            r.Headers.Add("X-Reporte", """{"operacion":"ia.superresolucion/v1"}""");
            return r;
        });
        var derivadoId = Guid.NewGuid();
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{EvidenciaId}/operaciones-ia", HttpStatusCode.Created, new
        {
            derivado = Evidencia(derivadoId, EvidenciaId, "cam1-x4.png", "SoloInvestigacion"),
            operacionId = Guid.NewGuid(), tipo = "ia.superresolucion/v1", parametros = "{}", entorno = "{}",
        });
        var pantalla = await PantallaAsync();

        await pantalla.SuperResolverCommand.ExecuteAsync(null);

        var visor = Assert.IsType<VisorViewModel>(_navegacion.Actual);
        Assert.Equal((derivadoId, ViaProcesamiento.SoloInvestigacion), (visor.Evidencia.Id, visor.Evidencia.Via));
        Assert.Equal("ia.superresolucion/v1 {}", visor.OperacionDeOrigen);
        // El nombre del derivado lo pone el servidor.
        Assert.DoesNotContain("name=nombre", _servidor.Cuerpos.Single());
    }

    [Fact]
    public async Task Volver_regresa_al_mismo_visor()
    {
        var pantalla = await PantallaAsync();

        pantalla.VolverCommand.Execute(null);

        Assert.Same(pantalla.Visor, _navegacion.Actual);
    }

    [Fact]
    public async Task Desde_el_visor_se_abre_la_pantalla_de_ia_de_esa_evidencia()
    {
        var visor = await VisorConContenidoAsync([1]);

        visor.AbrirAnalisisIaCommand.Execute(null);

        Assert.Same(visor, _navegacion.AnalisisIaDe);
    }

    [Fact]
    public async Task La_ventana_arma_la_pantalla_de_ia_sobre_el_visor_de_donde_se_vino()
    {
        var ventana = new Nucleo.VentanaViewModel(_api, workerIa: ClienteWorkerIa.Crear(new Uri("http://127.0.0.1:8100/"), _worker));
        var visor = await VisorConContenidoAsync([1]);

        ventana.IrAAnalisisIa(visor);

        Assert.Same(visor, Assert.IsType<AnalisisIaViewModel>(ventana.Actual).Visor);
    }
}
