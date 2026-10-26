using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Forensic.Application;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Workers;

namespace Forensic.IntegrationTests;

/// <summary>
/// Contrato de <c>POST /v1/operaciones</c>: <see cref="WorkerDeProcesamientoHttp"/> contra el worker
/// Python real (multipart de ida; PNG y cabeceras de vuelta), y su exigencia de todas las cabeceras.
/// </summary>
public class ContratoDelWorkerDeProcesamientoTests(WorkerDeProcesamientoReal worker) : IClassFixture<WorkerDeProcesamientoReal>
{
    private const string Recorte = "opencv.recorte/v1";
    private const string Parametros = """{"alto":4,"ancho":6,"x":1,"y":2}""";

    private WorkerDeProcesamientoHttp Cliente() => new(new HttpClient { BaseAddress = worker.UrlBase });

    [Fact]
    public async Task El_worker_real_devuelve_un_png_con_los_hashes_y_el_entorno_que_espera_el_servidor()
    {
        var png = await worker.GenerarPngAsync(ancho: 16, alto: 12, semilla: 1);

        await using var salida = await Cliente().AplicarAsync(Recorte, Parametros, new MemoryStream(png), CancellationToken.None);
        using var bytes = new MemoryStream();
        await salida.Contenido.CopyToAsync(bytes);

        Assert.Equal(HashSha256.Calcular(png), salida.HashEntrada);
        Assert.Equal(HashSha256.Calcular(bytes.ToArray()), salida.HashSalida);
        Assert.Equal(64, salida.HashPixeles.Length);
        var entorno = JsonDocument.Parse(salida.Entorno).RootElement;
        foreach (var clave in new[] { "worker", "python", "numpy", "opencv", "opencvBuildSha256", "hilos", "optimizado", "opencl", "cpu" })
            Assert.True(entorno.TryGetProperty(clave, out _), $"Falta '{clave}' en el entorno.");
    }

    [Fact]
    public async Task Un_rechazo_del_worker_real_es_procesamiento_rechazado()
    {
        await Assert.ThrowsAsync<ProcesamientoRechazadoException>(() =>
            Cliente().AplicarAsync(Recorte, Parametros, new MemoryStream(RandomNumberGenerator.GetBytes(64)), CancellationToken.None));
    }

    [Theory]
    [InlineData("X-Sha256-Entrada")]
    [InlineData("X-Sha256-Salida")]
    [InlineData("X-Sha256-Pixeles")]
    [InlineData("X-Entorno")]
    public async Task Una_respuesta_sin_una_cabecera_del_contrato_es_invalida(string ausente)
    {
        var cabeceras = new Dictionary<string, string>
        {
            ["X-Sha256-Entrada"] = new string('a', 64),
            ["X-Sha256-Salida"] = new string('b', 64),
            ["X-Sha256-Pixeles"] = new string('c', 64),
            ["X-Entorno"] = """{"opencv":"4.14.0"}""",
        };
        cabeceras.Remove(ausente);
        var cliente = new WorkerDeProcesamientoHttp(new HttpClient(new RespuestaConCabeceras(cabeceras)) { BaseAddress = new Uri("http://worker-falso/") });

        await Assert.ThrowsAsync<WorkerDeProcesamientoNoDisponibleException>(() =>
            cliente.AplicarAsync(Recorte, Parametros, new MemoryStream([1, 2, 3]), CancellationToken.None));
    }

    private sealed class RespuestaConCabeceras(Dictionary<string, string> cabeceras) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
        {
            await solicitud.Content!.CopyToAsync(Stream.Null, ct);
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x89, 0x50]) };
            foreach (var (nombre, valor) in cabeceras)
                respuesta.Headers.Add(nombre, valor);
            return respuesta;
        }
    }
}
