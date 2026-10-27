using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Forensic.IntegrationTests;

/// <summary>
/// El worker de IA real (<c>src/workers/ia</c>), construido desde su Dockerfile con los pesos clavados.
/// Sin GPU en los tests: corre en CPU con los mismos pesos.
/// </summary>
public sealed class WorkerDeIaReal : IAsyncLifetime
{
    private const ushort Puerto = 8000;

    /// <summary>Se reusa entre corridas (ver <see cref="ImagenesDeWorkers"/>): borrarla obligaba a bajar varios GB de pip y los pesos.</summary>
    public const string NombreDeLaImagen = "forensic-worker-ia:tests";

    private IContainer? _contenedor;

    private IContainer Contenedor => _contenedor ?? throw new InvalidOperationException("El worker no arrancó.");

    /// <summary>En la workstation el worker escucha en loopback: el puerto mapeado también lo está.</summary>
    public Uri UrlBase => new($"http://{Contenedor.Hostname}:{Contenedor.GetMappedPublicPort(Puerto)}/");

    public async Task InitializeAsync()
    {
        var imagen = await ImagenesDeWorkers.ConstruirAsync("src/workers/ia", NombreDeLaImagen);
        _contenedor = new ContainerBuilder(imagen)
            .WithPortBinding(Puerto, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Puerto).ForPath("/v1/entorno")))
            .Build();
        await _contenedor.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_contenedor is not null)
            await _contenedor.DisposeAsync();
        // La imagen no se libera: se reusa en la próxima corrida (ver ImagenesDeWorkers).
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
