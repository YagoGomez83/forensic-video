using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Forensic.Application.Casos;
using Forensic.Application.Evidencias;
using Forensic.Application.Operaciones;
using Forensic.Domain.Ia;

namespace Forensic.IntegrationTests;

/// <summary>
/// Un caso con material de las dos vías: una foto original, un recorte OpenCV (admisible), una
/// detección de IA sobre la foto, una super-resolución del recorte (sólo investigación) y un acceso
/// a ese derivado de IA.
/// </summary>
internal sealed record CasoMixto(CasoDto Caso, EvidenciaDto Original, EvidenciaDto Recorte, EvidenciaDto SuperRes)
{
    public const string RealEsrganSha256 = "1b21f3442cb75a7f76ae99cb7742a84510d14f428a6b2d3d8960440f7235a0bc";
    public const string YoloxSha256 = "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063";

    public static async Task<CasoMixto> CrearAsync(HttpClient ana)
    {
        var caso = await ana.AbrirCasoAsync();
        var original = await (await ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096), "cam1.png")).LeerAsync<EvidenciaDto>();
        var recorte = (await (await ana.AplicarOperacionAsync(
            caso.Id, original.Id, "opencv.recorte/v1", new { x = 1, y = 2, ancho = 3, alto = 4 })).LeerAsync<OperacionAplicadaDto>()).Derivado;

        var deteccion = $$"""
            {"analisis":"{{CatalogoDeIa.Deteccion}}","parametros":{"umbralConfianzaMilesimas":500},
             "modelo":{"id":"yolox-s","version":"0.1.1rc0","sha256":"{{YoloxSha256}}"},
             "entrada":{"sha256":"{{original.Hash}}","ancho":320,"alto":240},
             "entorno":{"onnxruntime":"1.31.0","proveedor":"CPUExecutionProvider"},
             "hallazgos":[{"region":{"x":1,"y":1,"ancho":2,"alto":2},"clase":"car","confianza":0.9,"crudo":{} }] }
            """;
        (await ana.PostAsync($"/casos/{caso.Id}/evidencias/{original.Id}/analisis-ia",
            new StringContent(deteccion, Encoding.UTF8, "application/json"))).EnsureSuccessStatusCode();

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. RandomNumberGenerator.GetBytes(512)];
        var reporte = $$"""
            {"operacion":"{{CatalogoDeIa.SuperResolucion}}","parametros":{},
             "modelo":{"id":"realesrgan-x4plus","version":"v0.1.0","sha256":"{{RealEsrganSha256}}"},
             "entrada":{"sha256":"{{recorte.Hash}}","ancho":3,"alto":4},
             "salida":{"sha256":"{{Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()}}","ancho":12,"alto":16},
             "entorno":{"onnxruntime":"1.31.0","proveedor":"CPUExecutionProvider"} }
            """;
        var salida = new ByteArrayContent(png);
        salida.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var superRes = (await (await ana.PostAsync($"/casos/{caso.Id}/evidencias/{recorte.Id}/operaciones-ia",
            new MultipartFormDataContent { { new StringContent(reporte), "reporte" }, { salida, "salida", "salida.png" } }))
            .LeerAsync<OperacionAplicadaDto>()).Derivado;

        (await ana.GetAsync($"/casos/{caso.Id}/evidencias/{superRes.Id}/contenido")).EnsureSuccessStatusCode();
        return new CasoMixto(caso, original, recorte, superRes);
    }
}
