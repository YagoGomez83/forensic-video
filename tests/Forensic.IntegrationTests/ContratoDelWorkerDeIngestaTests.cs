using System.Text.Json;
using Forensic.Application;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Workers;

namespace Forensic.IntegrationTests;

/// <summary>
/// Contrato JSON de <c>POST /v1/analizar</c>: <see cref="WorkerDeIngestaHttp"/> contra el worker
/// Python real. Si un lado renombra o cambia un campo, esto falla en vez de degradar en silencio
/// a «worker no disponible».
/// </summary>
public class ContratoDelWorkerDeIngestaTests(WorkerDeIngestaReal worker) : IClassFixture<WorkerDeIngestaReal>
{
    private WorkerDeIngestaHttp Cliente() => new(new HttpClient { BaseAddress = worker.UrlBase });

    private async Task<AnalisisDeIngesta> AnalizarAsync(byte[] bytes) =>
        await Cliente().AnalizarAsync(new MemoryStream(bytes), CancellationToken.None);

    [Fact]
    public async Task Un_video_legible_devuelve_hash_tamano_y_metadatos_de_ffprobe()
    {
        var video = await worker.GenerarVideoAsync();

        var analisis = await AnalizarAsync(video);

        Assert.Equal(HashSha256.Calcular(video), analisis.Hash);
        Assert.Equal(video.LongLength, analisis.TamanoBytes);
        Assert.Null(analisis.MotivoSinMetadatos);
        var metadatos = Assert.NotNull(analisis.Metadatos);
        Assert.False(string.IsNullOrEmpty(metadatos.GetProperty("version").GetString()));
        Assert.Contains("mp4", metadatos.GetProperty("formato").GetProperty("format_name").GetString());
        var video0 = Assert.Single(metadatos.GetProperty("streams").EnumerateArray());
        Assert.Equal("video", video0.GetProperty("codec_type").GetString());
        Assert.Equal(64, video0.GetProperty("width").GetInt32());
    }

    [Fact]
    public async Task Un_formato_que_ffprobe_no_lee_devuelve_el_hash_y_el_motivo()
    {
        // Cabecera de un export propietario de DVR (Dahua): ffprobe no lo reconoce.
        var bytes = "DHAV"u8.ToArray().Concat(Enumerable.Repeat((byte)0x02, 1024)).ToArray();

        var analisis = await AnalizarAsync(bytes);

        Assert.Equal(HashSha256.Calcular(bytes), analisis.Hash);
        Assert.Equal(bytes.LongLength, analisis.TamanoBytes);
        Assert.True(analisis.Metadatos is null || analisis.Metadatos.Value.ValueKind == JsonValueKind.Null);
        Assert.False(string.IsNullOrWhiteSpace(analisis.MotivoSinMetadatos));
    }

    [Fact]
    public async Task Una_evidencia_vacia_la_rechaza_el_worker()
    {
        await Assert.ThrowsAsync<IngestaRechazadaException>(() => AnalizarAsync([]));
    }
}
