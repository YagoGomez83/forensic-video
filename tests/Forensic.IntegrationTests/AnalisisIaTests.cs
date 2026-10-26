using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forensic.Api.Controllers;
using Forensic.Application.Evidencias;
using Forensic.Application.Ia;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.IntegrationTests;

/// <summary>
/// El cliente corre el worker de IA en la workstation y reporta el resultado; el servidor no puede
/// re-ejecutarlo, así que verifica lo que sí puede (que se analizó la evidencia custodiada, que el
/// modelo está en la lista blanca, permisos) y asienta todo como «sólo investigación».
/// </summary>
public class AnalisisIaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private const string YoloxSha256 = "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063";

    // Con espacios y la confianza con todos sus dígitos, como los escribe el worker: llegan así al asiento.
    private const string CrudoAuto =
        """{"caja": [34.2, 18.2, 50.2, 26.2], "claseIndice": 2, "objetividad": 0.9, "puntajeClase": 0.9126, "puntaje": 0.8213145732879639}""";
    private const string Entorno = """{"onnxruntime": "1.31.0", "proveedor": "CPUExecutionProvider", "gpu": null}""";

    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    private async Task<(Guid CasoId, EvidenciaDto Original)> CasoConOriginalAsync()
    {
        var caso = await _ana.AbrirCasoAsync();
        var original = await (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096), "cam1.png"))
            .LeerAsync<EvidenciaDto>();
        return (caso.Id, original);
    }

    /// <summary>El JSON tal como lo devuelve <c>POST /v1/detectar</c> del worker de IA.</summary>
    private static string Reporte(
        string sha256Entrada, string hallazgos = $$"""[{"region": {"x": 34, "y": 18, "ancho": 17, "alto": 9}, "clase": "car", "confianza": 0.8213145732879639, "crudo": {{CrudoAuto}}}]""",
        string analisis = CatalogoDeIa.Deteccion, string modelo = "yolox-s", string version = "0.1.1rc0",
        string sha256Modelo = YoloxSha256, string entorno = Entorno, string parametros = """{"umbralConfianzaMilesimas": 500}""") =>
        $$"""
        {
          "analisis": "{{analisis}}",
          "parametros": {{parametros}},
          "parametrosCanonicos": "ignorado por el servidor",
          "modelo": {"id": "{{modelo}}", "version": "{{version}}", "sha256": "{{sha256Modelo}}"},
          "entrada": {"sha256": "{{sha256Entrada}}", "ancho": 320, "alto": 240},
          "entorno": {{entorno}},
          "hallazgos": {{hallazgos}}
        }
        """;

    private static Task<HttpResponseMessage> RegistrarAsync(HttpClient cliente, Guid casoId, Guid evidenciaId, string reporte) =>
        cliente.PostAsync(
            $"/casos/{casoId}/evidencias/{evidenciaId}/analisis-ia",
            new StringContent(reporte, Encoding.UTF8, "application/json"));

    private async Task<List<AnalisisIaDto>> ListarAsync(HttpClient cliente, Guid casoId, Guid evidenciaId) =>
        await (await cliente.GetAsync($"/casos/{casoId}/evidencias/{evidenciaId}/analisis-ia")).LeerAsync<List<AnalisisIaDto>>();

    [Fact]
    public async Task Registrar_asienta_la_procedencia_y_los_hallazgos_como_solo_investigacion()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var leido = """{"region": {"x": 1, "y": 2, "ancho": 80, "alto": 20}, "textoLeido": "ABC123", "confianza": 0.82, "crudo": {"texto": "ABC123"}}""";
        var hallazgos = $$"""[{"region": {"x": 34, "y": 18, "ancho": 17, "alto": 9}, "clase": "car", "confianza": 0.8213145732879639, "crudo": {{CrudoAuto}}}, {{leido}}]""";

        var r = await RegistrarAsync(_ana, casoId, original.Id, Reporte(original.Hash, hallazgos));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var analisis = await r.LeerAsync<AnalisisIaDto>();
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, analisis.Via);
        Assert.Equal(original.Id, analisis.EvidenciaId);
        Assert.Equal(CatalogoDeIa.Deteccion, analisis.Analisis);
        Assert.Equal("""{"umbralConfianzaMilesimas":500}""", analisis.Parametros);
        Assert.Equal(new ModeloDeIaDto("yolox-s", "0.1.1rc0", YoloxSha256), analisis.Modelo);
        Assert.Equal(Entorno, analisis.Entorno);
        Assert.Equal("ana", analisis.Actor);
        Assert.Collection(
            analisis.Hallazgos,
            h =>
            {
                Assert.Equal(new RegionDto(34, 18, 17, 9), h.Region);
                Assert.Equal("car", h.Clase);
                Assert.Null(h.TextoLeido);
                Assert.Equal(0.8213145732879639m, h.Confianza);
                Assert.Equal(CrudoAuto, h.Crudo);
                Assert.Equal(ViaProcesamiento.SoloInvestigacion, h.Via);
            },
            h =>
            {
                Assert.Equal("ABC123", h.TextoLeido);
                Assert.Null(h.Clase);
                Assert.Equal(0.82m, h.Confianza);
            });

        var export = await _ana.ExportarAsync(casoId);
        Assert.True(export.Verificacion.EsIntegra);
        var asiento = export.Asientos[^1];
        Assert.Equal(nameof(TipoAsiento.HallazgosRegistrados), asiento.Tipo);
        Assert.Equal(original.Id, asiento.EvidenciaId);
        Assert.Contains(CrudoAuto, asiento.Detalle);
        Assert.Contains("0.8213145732879639", asiento.Detalle);
        Assert.Contains("declarada-por-workstation", asiento.Detalle);

        var listados = await ListarAsync(_ana, casoId, original.Id);
        Assert.Equal(analisis.Id, Assert.Single(listados).Id);
        Assert.Equal(analisis.Hallazgos, listados[0].Hallazgos);
    }

    [Fact]
    public async Task Un_analisis_sin_hallazgos_tambien_se_asienta()
    {
        var (casoId, original) = await CasoConOriginalAsync();

        var r = await RegistrarAsync(_ana, casoId, original.Id, Reporte(original.Hash, hallazgos: "[]"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Empty((await r.LeerAsync<AnalisisIaDto>()).Hallazgos);
        Assert.Equal(nameof(TipoAsiento.HallazgosRegistrados), (await _ana.ExportarAsync(casoId)).Asientos[^1].Tipo);
    }

    [Fact]
    public async Task Si_el_worker_analizo_otro_contenido_no_se_registra_nada()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var antes = (await _ana.ExportarAsync(casoId)).Asientos.Count;
        var otro = Convert.ToHexString(SHA256.HashData("otra cosa"u8)).ToLowerInvariant();

        var r = await RegistrarAsync(_ana, casoId, original.Id, Reporte(otro));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(antes, (await _ana.ExportarAsync(casoId)).Asientos.Count);
        Assert.Empty(await ListarAsync(_ana, casoId, original.Id));
    }

    public static TheoryData<string, string> ReportesInvalidos => new()
    {
        // Modelo fuera de la lista blanca, o con otros pesos.
        { "modelo", "yolo-pirata" },
        { "pesos", Convert.ToHexString(SHA256.HashData("otros pesos"u8)).ToLowerInvariant() },
        { "version", "0.1.0" },
        // Análisis fuera del catálogo de IA (también uno determinista: los catálogos no se cruzan).
        { "analisis", "ia.reconocimiento-facial/v1" },
        { "analisis", "opencv.recorte/v1" },
        // Parámetros, entorno, hash mal formado, hallazgos inválidos.
        { "parametros", """{"umbralConfianzaMilesimas": 0}""" },
        { "entorno", "\"no es un objeto\"" },
        { "pesos", "NO-ES-UN-HASH" },
        { "hallazgos", """[{"region": {"x": 0, "y": 0, "ancho": 1, "alto": 1}, "clase": "car", "confianza": 1.5, "crudo": {}}]""" },
        { "hallazgos", """[{"region": {"x": 0, "y": 0, "ancho": 0, "alto": 1}, "clase": "car", "confianza": 0.5, "crudo": {}}]""" },
        { "hallazgos", """[{"region": {"x": 0, "y": 0, "ancho": 1, "alto": 1}, "clase": "car", "textoLeido": "ABC", "confianza": 0.5, "crudo": {}}]""" },
        { "hallazgos", """[{"region": {"x": 0, "y": 0, "ancho": 1, "alto": 1}, "confianza": 0.5, "crudo": {}}]""" },
        { "hallazgos", """[{"region": {"x": 0, "y": 0, "ancho": 1, "alto": 1}, "clase": "car", "confianza": 0.5, "crudo": [1, 2]}]""" },
        { "hallazgos", """[{"region": {"x": 0, "y": 0, "ancho": 1, "alto": 1}, "clase": "car", "confianza": 0.5}]""" },
    };

    [Theory]
    [MemberData(nameof(ReportesInvalidos))]
    public async Task Un_reporte_invalido_se_rechaza_sin_asentar(string campo, string valor)
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var antes = (await _ana.ExportarAsync(casoId)).Asientos.Count;
        var reporte = campo switch
        {
            "modelo" => Reporte(original.Hash, modelo: valor),
            "pesos" => Reporte(original.Hash, sha256Modelo: valor),
            "version" => Reporte(original.Hash, version: valor),
            "analisis" => Reporte(original.Hash, analisis: valor),
            "parametros" => Reporte(original.Hash, parametros: valor),
            "entorno" => Reporte(original.Hash, entorno: valor),
            "hallazgos" => Reporte(original.Hash, hallazgos: valor),
            _ => throw new ArgumentException(campo),
        };

        var r = await RegistrarAsync(_ana, casoId, original.Id, reporte);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(antes, (await _ana.ExportarAsync(casoId)).Asientos.Count);
    }

    [Fact]
    public async Task Un_tipo_de_ia_no_entra_por_las_operaciones_deterministas()
    {
        var (casoId, original) = await CasoConOriginalAsync();

        var r = await _ana.AplicarOperacionAsync(casoId, original.Id, CatalogoDeIa.Deteccion, new { umbralConfianzaMilesimas = 500 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
    }

    [Fact]
    public async Task Solo_el_analista_asignado_con_el_caso_abierto_registra_y_quien_lee_el_caso_lista()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var reporte = Reporte(original.Hash);
        (await RegistrarAsync(_ana, casoId, original.Id, reporte)).EnsureSuccessStatusCode();

        foreach (var (usuario, rol) in new[] { ("beto", Rol.Analista), ("sofia", Rol.Supervisor), ("aurelio", Rol.Auditor) })
            Assert.Equal(HttpStatusCode.Forbidden, (await RegistrarAsync(servidor.ClienteDe(usuario, rol), casoId, original.Id, reporte)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await servidor.ClienteDe("beto", Rol.Analista).GetAsync($"/casos/{casoId}/evidencias/{original.Id}/analisis-ia")).StatusCode);
        Assert.Single(await ListarAsync(servidor.ClienteDe("aurelio", Rol.Auditor), casoId, original.Id));
        Assert.Single(await ListarAsync(servidor.ClienteDe("sofia", Rol.Supervisor), casoId, original.Id));

        (await servidor.ClienteDe("sofia", Rol.Supervisor).PostAsync($"/casos/{casoId}/cerrar", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await RegistrarAsync(_ana, casoId, original.Id, reporte)).StatusCode);
    }

    [Fact]
    public async Task Una_evidencia_inexistente_o_de_otro_caso_da_404()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var (otroCaso, _) = await CasoConOriginalAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await RegistrarAsync(_ana, otroCaso, original.Id, Reporte(original.Hash))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RegistrarAsync(_ana, casoId, Guid.NewGuid(), Reporte(original.Hash))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _ana.GetAsync($"/casos/{casoId}/evidencias/{Guid.NewGuid()}/analisis-ia")).StatusCode);
    }

    [Fact]
    public void El_reporte_tiene_tope_de_tamano()
    {
        // TestServer no implementa el límite de cuerpo (lo aplica Kestrel), así que se fija el atributo.
        var accion = typeof(AnalisisIaController).GetMethod(nameof(AnalisisIaController.Registrar))!;

        Assert.Single(accion.GetCustomAttributes(typeof(RequestSizeLimitAttribute), false));
        Assert.Equal(4 * 1024 * 1024, AnalisisIaController.TamanoMaximoDelReporte);
    }
}
