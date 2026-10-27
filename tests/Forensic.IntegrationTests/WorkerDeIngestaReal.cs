using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Forensic.IntegrationTests;

/// <summary>
/// El worker de ingesta real (<c>src/workers/ingest</c>), construido desde su Dockerfile: la misma
/// imagen que corre en el compose, con FastAPI y ffprobe de verdad.
/// </summary>
public sealed class WorkerDeIngestaReal : IAsyncLifetime
{
    private const ushort Puerto = 8000;

    /// <summary>Se reusa entre corridas (ver <see cref="ImagenesDeWorkers"/>).</summary>
    public const string NombreDeLaImagen = "forensic-worker-ingesta:tests";

    private IContainer? _contenedor;

    private IContainer Contenedor => _contenedor ?? throw new InvalidOperationException("El worker no arrancó.");

    public Uri UrlBase => new($"http://{Contenedor.Hostname}:{Contenedor.GetMappedPublicPort(Puerto)}/");

    public async Task InitializeAsync()
    {
        var imagen = await ImagenesDeWorkers.ConstruirAsync("src/workers/ingest", NombreDeLaImagen);
        _contenedor = new ContainerBuilder(imagen)
            .WithPortBinding(Puerto, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Puerto).ForPath("/salud")))
            .Build();
        await _contenedor.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_contenedor is not null)
            await _contenedor.DisposeAsync();
        // La imagen no se libera: se reusa en la próxima corrida (ver ImagenesDeWorkers).
    }

    /// <summary>Genera un video MP4 real con el ffmpeg de la propia imagen y devuelve sus bytes.</summary>
    public async Task<byte[]> GenerarVideoAsync()
    {
        const string ruta = "/tmp/muestra.mp4";
        var r = await Contenedor.ExecAsync(
            ["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc=duration=1:size=64x48:rate=5", "-c:v", "mpeg4", "-y", ruta]);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg falló: {r.Stderr}");
        return await Contenedor.ReadFileAsync(ruta);
    }
}
