using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Forensic.IntegrationTests;

/// <summary>
/// El worker de procesamiento real (<c>src/workers/procesamiento</c>), construido desde su Dockerfile:
/// la misma imagen del compose, con OpenCV de verdad.
/// </summary>
public sealed class WorkerDeProcesamientoReal : IAsyncLifetime
{
    private const ushort Puerto = 8000;

    /// <summary>Se reusa entre corridas (ver <see cref="ImagenesDeWorkers"/>).</summary>
    public const string NombreDeLaImagen = "forensic-worker-procesamiento:tests";

    private IContainer? _contenedor;

    private IContainer Contenedor => _contenedor ?? throw new InvalidOperationException("El worker no arrancó.");

    public Uri UrlBase => new($"http://{Contenedor.Hostname}:{Contenedor.GetMappedPublicPort(Puerto)}/");

    public async Task InitializeAsync()
    {
        var imagen = await ImagenesDeWorkers.ConstruirAsync("src/workers/procesamiento", NombreDeLaImagen);
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

    /// <summary>Un video FFV1 (lossless) con frames de ruido, generado con el OpenCV de la propia imagen.</summary>
    public async Task<byte[]> GenerarVideoAsync(int frames)
    {
        const string ruta = "/tmp/muestra.mkv";
        var r = await Contenedor.ExecAsync(
        [
            "python", "-c",
            "import sys, numpy as np, cv2; " +
            $"w = cv2.VideoWriter('{ruta}', cv2.VideoWriter_fourcc(*'FFV1'), 10, (64, 48)); " +
            "g = np.random.default_rng(7); " +
            "[w.write(g.integers(0, 256, (48, 64, 3), dtype=np.uint8)) for _ in range(int(sys.argv[1]))]; " +
            "w.release()",
            frames.ToString(),
        ]);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"No se pudo generar el video: {r.Stderr}");
        return await Contenedor.ReadFileAsync(ruta);
    }

    /// <summary>Una imagen PNG real (ruido BGR con semilla fija), generada con el OpenCV de la propia imagen.</summary>
    public async Task<byte[]> GenerarPngAsync(int ancho, int alto, int semilla)
    {
        const string ruta = "/tmp/muestra.png";
        var r = await Contenedor.ExecAsync(
        [
            "python", "-c",
            "import sys, numpy as np, cv2; " +
            "a = np.random.default_rng(int(sys.argv[3])).integers(0, 256, (int(sys.argv[2]), int(sys.argv[1]), 3), dtype=np.uint8); " +
            $"cv2.imwrite('{ruta}', a)",
            ancho.ToString(), alto.ToString(), semilla.ToString(),
        ]);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"No se pudo generar el PNG: {r.Stderr}");
        return await Contenedor.ReadFileAsync(ruta);
    }
}
