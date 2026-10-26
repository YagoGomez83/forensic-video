using System.Net;
using System.Text.Json;
using Forensic.Application.Evidencias;
using Forensic.Application.Operaciones;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Forensic.IntegrationTests;

/// <summary>
/// El gate de la Fase 2 contra OpenCV real: el servidor completo con el cliente HTTP real y la imagen
/// Docker del worker de procesamiento. Se aplica una operación, se reproduce desde su asiento y el
/// hash coincide byte a byte.
/// </summary>
public sealed class ReproduccionConOpenCvTests : IClassFixture<ServidorForense>, IClassFixture<WorkerDeProcesamientoReal>, IAsyncDisposable
{
    private readonly WorkerDeProcesamientoReal _worker;
    private readonly WebApplicationFactory<Program> _conOpenCv;
    private readonly HttpClient _ana;

    public ReproduccionConOpenCvTests(ServidorForense servidor, WorkerDeProcesamientoReal worker)
    {
        _worker = worker;
        _conOpenCv = servidor.WithWebHostBuilder(b =>
        {
            b.UseSetting("WorkerProcesamiento:UrlBase", worker.UrlBase.ToString());
            b.ConfigureTestServices(s => s.AddHttpClient<IWorkerDeProcesamiento, WorkerDeProcesamientoHttp>(
                (sp, http) => http.BaseAddress = worker.UrlBase));
        });
        _ana = _conOpenCv.CreateClient().ComoActor("ana", Rol.Analista);
    }

    public ValueTask DisposeAsync() => _conOpenCv.DisposeAsync();

    private async Task<(Guid CasoId, EvidenciaDto Original)> CasoConImagenAsync()
    {
        var caso = await _ana.AbrirCasoAsync();
        var png = await _worker.GenerarPngAsync(ancho: 64, alto: 48, semilla: 2026);
        return (caso.Id, await (await _ana.IngestarAsync(caso.Id, png, "cam1.png")).LeerAsync<EvidenciaDto>());
    }

    [Fact]
    public async Task Un_recorte_con_opencv_se_reproduce_byte_a_byte_desde_su_asiento()
    {
        var (casoId, original) = await CasoConImagenAsync();
        var aplicada = await (await _ana.AplicarOperacionAsync(casoId, original.Id, "opencv.recorte/v1", new { x = 5, y = 10, ancho = 30, alto = 20 }))
            .LeerAsync<OperacionAplicadaDto>();

        var resultado = await (await _ana.ReproducirAsync(casoId, aplicada.Derivado.Id)).LeerAsync<ResultadoDeReproduccion>();

        Assert.True(resultado.Reproducido, $"esperado {resultado.Sha256Esperado}, obtenido {resultado.Sha256Obtenido}");
        Assert.True(resultado.EntornoCoincide);
        Assert.Equal(aplicada.Derivado.Hash, resultado.Sha256Obtenido);
        Assert.Equal(resultado.Sha256PixelesEsperado, resultado.Sha256PixelesObtenido);

        // El derivado es un PNG real y el entorno asentado es el de OpenCV clavado.
        var bytes = await (await _ana.GetAsync($"/casos/{casoId}/evidencias/{aplicada.Derivado.Id}/contenido")).Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        var entorno = JsonDocument.Parse(aplicada.Entorno).RootElement;
        Assert.Equal("4.14.0", entorno.GetProperty("opencv").GetString());
        Assert.Equal((1, false), (entorno.GetProperty("hilos").GetInt32(), entorno.GetProperty("optimizado").GetBoolean()));
    }

    [Fact]
    public async Task Una_region_fuera_de_la_imagen_la_rechaza_el_worker_real_con_422()
    {
        var (casoId, original) = await CasoConImagenAsync();

        var r = await _ana.AplicarOperacionAsync(casoId, original.Id, "opencv.recorte/v1", new { x = 50, y = 0, ancho = 30, alto = 20 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
    }
}
