using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Forensic.Application.Evidencias;
using Forensic.Application.Operaciones;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>
/// Operaciones de IA que producen un derivado (super-resolución): el worker de la workstation devuelve
/// el PNG y su reporte, el cliente los reenvía, y el servidor hashea el PNG, valida la procedencia,
/// custodia el derivado «sólo investigación» y asienta OperacionIaAplicada. Nunca se reproduce.
/// </summary>
public class OperacionesIaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private const string RealEsrganSha256 = "1b21f3442cb75a7f76ae99cb7742a84510d14f428a6b2d3d8960440f7235a0bc";
    private const string Entorno = """{"onnxruntime": "1.31.0", "proveedor": "CUDAExecutionProvider"}""";

    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    private async Task<(Guid CasoId, EvidenciaDto Original)> CasoConOriginalAsync()
    {
        var caso = await _ana.AbrirCasoAsync();
        var original = await (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096), "cam1.png"))
            .LeerAsync<EvidenciaDto>();
        return (caso.Id, original);
    }

    /// <summary>Un PNG para el servidor: lo que importa es la firma (el servidor no decodifica imágenes).</summary>
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. RandomNumberGenerator.GetBytes(512)];

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>El reporte tal como lo manda el worker en la cabecera X-Reporte.</summary>
    private static string Reporte(
        string sha256Entrada, string sha256Salida, string operacion = CatalogoDeIa.SuperResolucion,
        string modelo = "realesrgan-x4plus", string version = "v0.1.0", string sha256Modelo = RealEsrganSha256,
        string parametros = "{}", string entorno = Entorno) =>
        $$"""
        {"operacion":"{{operacion}}","parametros":{{parametros}},
         "modelo":{"id":"{{modelo}}","version":"{{version}}","sha256":"{{sha256Modelo}}"},
         "entrada":{"sha256":"{{sha256Entrada}}","ancho":16,"alto":12},
         "salida":{"sha256":"{{sha256Salida}}","ancho":64,"alto":48},
         "entorno":{{entorno}}}
        """;

    private static Task<HttpResponseMessage> RegistrarAsync(
        HttpClient cliente, Guid casoId, Guid evidenciaId, string reporte, byte[] png, string? nombre = null)
    {
        var salida = new ByteArrayContent(png);
        salida.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var formulario = new MultipartFormDataContent { { new StringContent(reporte), "reporte" }, { salida, "salida", "salida.png" } };
        if (nombre is not null)
            formulario.Add(new StringContent(nombre), "nombre");
        return cliente.PostAsync($"/casos/{casoId}/evidencias/{evidenciaId}/operaciones-ia", formulario);
    }

    [Fact]
    public async Task Una_super_resolucion_crea_un_derivado_solo_investigacion_con_su_procedencia_asentada()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var png = Png();

        var r = await RegistrarAsync(_ana, casoId, original.Id, Reporte(original.Hash, Hex(png)), png);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var aplicada = await r.LeerAsync<OperacionAplicadaDto>();
        var derivado = aplicada.Derivado;
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, derivado.Via);
        Assert.Equal(original.Id, derivado.PadreId);
        Assert.Equal(Hex(png), derivado.Hash);
        Assert.Equal("cam1-superresolucion.png", derivado.NombreOriginal);
        Assert.Equal((CatalogoDeIa.SuperResolucion, "{}", Entorno), (aplicada.Tipo, aplicada.Parametros, aplicada.Entorno));
        // El original no cambia de vía: lo de IA vive en el derivado.
        Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, (await _ana.EvidenciasAsync(casoId)).Single(e => e.Id == original.Id).Via);
        Assert.Equal(png, await (await _ana.GetAsync($"/casos/{casoId}/evidencias/{derivado.Id}/contenido")).Content.ReadAsByteArrayAsync());

        var export = await _ana.ExportarAsync(casoId);
        Assert.True(export.Verificacion.EsIntegra);
        var asiento = export.Asientos.Single(a => a.Tipo == nameof(TipoAsiento.OperacionIaAplicada));
        Assert.Equal(derivado.Id, asiento.EvidenciaId);
        var detalle = JsonDocument.Parse(asiento.Detalle).RootElement;
        Assert.Equal("forensic-video/operacion-ia/v1", detalle.GetProperty("formato").GetString());
        Assert.Equal(RealEsrganSha256, detalle.GetProperty("modelo").GetProperty("sha256").GetString());
        Assert.Equal(Entorno, detalle.GetProperty("procedencia").GetProperty("entorno").GetRawText());
        Assert.Equal("SoloInvestigacion", detalle.GetProperty("via").GetString());
    }

    [Fact]
    public async Task Un_derivado_de_ia_no_se_reproduce_y_lo_dice()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var png = Png();
        var derivado = (await (await RegistrarAsync(_ana, casoId, original.Id, Reporte(original.Hash, Hex(png)), png)).LeerAsync<OperacionAplicadaDto>()).Derivado;

        var r = await _ana.ReproducirAsync(casoId, derivado.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("IA", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Procesar_con_opencv_un_derivado_de_ia_sigue_siendo_solo_investigacion_y_el_linaje_muestra_el_modelo()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var png = Png();
        var superRes = (await (await RegistrarAsync(_ana, casoId, original.Id, Reporte(original.Hash, Hex(png)), png)).LeerAsync<OperacionAplicadaDto>()).Derivado;

        var recorte = await (await _ana.AplicarOperacionAsync(casoId, superRes.Id, "opencv.recorte/v1", new { x = 0, y = 0, ancho = 4, alto = 4 }))
            .LeerAsync<OperacionAplicadaDto>();

        Assert.Equal(ViaProcesamiento.SoloInvestigacion, recorte.Derivado.Via);
        // La operación determinista sobre un derivado de IA sí se reproduce (es OpenCV), y sigue fuera de la vía judicial.
        Assert.True((await (await _ana.ReproducirAsync(casoId, recorte.Derivado.Id)).LeerAsync<ResultadoDeReproduccion>()).Reproducido);

        var linaje = await (await _ana.GetAsync($"/casos/{casoId}/evidencias/{recorte.Derivado.Id}/linaje")).LeerAsync<List<EslabonDeLinaje>>();
        Assert.Equal([original.Id, superRes.Id, recorte.Derivado.Id], linaje.Select(e => e.Evidencia.Id));
        var ia = linaje[1].Operacion!;
        Assert.Equal((CatalogoDeIa.SuperResolucion, ViaProcesamiento.SoloInvestigacion), (ia.Tipo, ia.Via));
        Assert.Equal("realesrgan-x4plus", ia.Modelo!.Id);
        Assert.Equal(RealEsrganSha256, ia.Modelo.Sha256);
        Assert.Null(linaje[2].Operacion!.Modelo);
    }

    public static TheoryData<string> CamposInvalidos => new()
    {
        "salida", "entrada", "png", "modelo", "modelo-de-otra-capacidad", "determinista", "analisis", "parametros", "entorno", "json",
    };

    [Theory]
    [MemberData(nameof(CamposInvalidos))]
    public async Task Un_reporte_o_un_png_invalido_se_rechaza_sin_asentar(string caso)
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var antes = (await _ana.ExportarAsync(casoId)).Asientos.Count;
        var png = Png();
        var otro = Hex([1, 2, 3]);
        var (reporte, cuerpo) = caso switch
        {
            // El PNG recibido no es el que reportó el worker, o el worker procesó otra cosa.
            "salida" => (Reporte(original.Hash, otro), png),
            "entrada" => (Reporte(otro, Hex(png)), png),
            "png" => (Reporte(original.Hash, Hex([1, 2, 3])), new byte[] { 1, 2, 3 }),
            "modelo" => (Reporte(original.Hash, Hex(png), sha256Modelo: otro), png),
            "modelo-de-otra-capacidad" => (Reporte(original.Hash, Hex(png), modelo: "yolox-s", version: "0.1.1rc0",
                sha256Modelo: "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"), png),
            "determinista" => (Reporte(original.Hash, Hex(png), operacion: "opencv.recorte/v1"), png),
            "analisis" => (Reporte(original.Hash, Hex(png), operacion: CatalogoDeIa.Deteccion), png),
            "parametros" => (Reporte(original.Hash, Hex(png), parametros: """{"escala":4}"""), png),
            "entorno" => (Reporte(original.Hash, Hex(png), entorno: "[]"), png),
            "json" => ("no es json", png),
            _ => throw new ArgumentException(caso),
        };

        var r = await RegistrarAsync(_ana, casoId, original.Id, reporte, cuerpo);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(antes, (await _ana.ExportarAsync(casoId)).Asientos.Count);
        Assert.Single(await _ana.EvidenciasAsync(casoId));
    }

    [Fact]
    public async Task Solo_el_analista_asignado_con_el_caso_abierto_registra()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var png = Png();
        var reporte = Reporte(original.Hash, Hex(png));

        foreach (var (usuario, rol) in new[] { ("beto", Rol.Analista), ("sofia", Rol.Supervisor), ("aurelio", Rol.Auditor) })
            Assert.Equal(HttpStatusCode.Forbidden, (await RegistrarAsync(servidor.ClienteDe(usuario, rol), casoId, original.Id, reporte, png)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RegistrarAsync(_ana, casoId, Guid.NewGuid(), reporte, png)).StatusCode);
    }

    [Fact]
    public async Task La_base_rechaza_una_operacion_de_ia_admisible_o_una_determinista_con_modelo()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var png = Png();
        (await RegistrarAsync(_ana, casoId, original.Id, Reporte(original.Hash, Hex(png)), png)).EnsureSuccessStatusCode();
        await using var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();

        // Copia de la operación de IA con la vía cambiada: el CHECK la rechaza.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "OperacionesForenses"
                ("Id", "CasoId", "EvidenciaEntradaId", "HashEntrada", "Tipo", "Entorno", "Parametros", "Via", "HashSalida",
                 "TamanoSalidaBytes", "HashPixelesSalida", "Actor", "RegistradaEn", "ModeloId", "ModeloVersion", "ModeloSha256")
            SELECT gen_random_uuid(), "CasoId", "EvidenciaEntradaId", "HashEntrada", "Tipo", "Entorno", "Parametros", 'AdmisibleEnJuicio',
                   "HashSalida", "TamanoSalidaBytes", "HashPixelesSalida", "Actor", "RegistradaEn", "ModeloId", "ModeloVersion", "ModeloSha256"
            FROM "OperacionesForenses" WHERE "CasoId" = @caso
            """, conexion);
        cmd.Parameters.AddWithValue("caso", casoId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }
}
