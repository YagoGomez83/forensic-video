using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;

namespace Forensic.IntegrationTests;

/// <summary>
/// Las imágenes de los workers, construidas desde su Dockerfile con un nombre fijo y sin borrarlas: así
/// Docker reusa su caché de capas entre corridas (que se invalida por contenido: si cambia el Dockerfile,
/// las dependencias o el código, se rehace desde esa capa). Cada imagen se construye una sola vez por
/// proceso: dos fixtures que la construyen a la vez chocan en el tar del contexto, que Testcontainers
/// escribe en %TEMP% con el nombre de la imagen.
/// </summary>
internal static class ImagenesDeWorkers
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<IFutureDockerImage>>> Construidas = new();

    /// <param name="carpeta">El directorio del worker relativo a la raíz del repositorio (su contexto de build).</param>
    public static Task<IFutureDockerImage> ConstruirAsync(string carpeta, string nombre) =>
        Construidas.GetOrAdd(nombre, _ => new Lazy<Task<IFutureDockerImage>>(async () =>
        {
            var imagen = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(CommonDirectoryPath.GetGitDirectory(), carpeta)
                .WithDockerfile("Dockerfile")
                .WithName(nombre)
                .WithDeleteIfExists(false)
                .WithCleanUp(false)
                .Build();
            await imagen.CreateAsync();
            return imagen;
        })).Value;
}
