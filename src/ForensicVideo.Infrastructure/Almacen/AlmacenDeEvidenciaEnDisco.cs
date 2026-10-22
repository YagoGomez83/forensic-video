using System.Buffers;
using System.Security.Cryptography;
using ForensicVideo.Application;
using ForensicVideo.Domain.Comun;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForensicVideo.Infrastructure.Almacen;

public sealed class OpcionesAlmacen
{
    public const string Seccion = "AlmacenDeEvidencia";

    /// <summary>Directorio raíz de los blobs. Debe estar fuera del alcance de escritura de los analistas.</summary>
    public string RutaRaiz { get; set; } = "";
}

/// <summary>
/// Blobs en disco direccionados por su SHA-256 (<c>raiz/ab/cd/abcd…</c>). La ruta se deriva sólo
/// del hash: el nombre que trae la evidencia nunca toca el sistema de archivos.
/// </summary>
internal sealed class AlmacenDeEvidenciaEnDisco(
    IOptions<OpcionesAlmacen> opciones, ILogger<AlmacenDeEvidenciaEnDisco> logger) : IAlmacenDeEvidencia
{
    private const int TamanoBuffer = 1024 * 1024;

    private readonly string _raiz = Path.GetFullPath(
        string.IsNullOrWhiteSpace(opciones.Value.RutaRaiz)
            ? throw new InvalidOperationException($"Falta configurar {OpcionesAlmacen.Seccion}:RutaRaiz.")
            : opciones.Value.RutaRaiz);

    public async Task<ContenidoAlmacenado> GuardarAsync(Stream contenido, CancellationToken ct)
    {
        var dirIngesta = Directory.CreateDirectory(Path.Combine(_raiz, ".ingesta")).FullName;
        var temporal = Path.Combine(dirIngesta, $"{Guid.NewGuid():N}.tmp");
        try
        {
            HashSha256 hash;
            long tamano = 0;
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (var destino = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None, TamanoBuffer, useAsync: true))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(TamanoBuffer);
                try
                {
                    int leidos;
                    while ((leidos = await contenido.ReadAsync(buffer.AsMemory(0, TamanoBuffer), ct)) > 0)
                    {
                        sha.AppendData(buffer, 0, leidos);
                        await destino.WriteAsync(buffer.AsMemory(0, leidos), ct);
                        tamano += leidos;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                await destino.FlushAsync(ct);
                destino.Flush(flushToDisk: true);
                hash = HashSha256.DesdeBytes(sha.GetHashAndReset());
            }

            var ruta = RutaDe(hash);
            if (File.Exists(ruta))
            {
                // Mismo contenido ya almacenado (p. ej. la misma evidencia en otro caso).
                File.Delete(temporal);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
                File.Move(temporal, ruta);
                File.SetAttributes(ruta, FileAttributes.ReadOnly);
            }
            return new ContenidoAlmacenado(hash, tamano);
        }
        catch
        {
            TryBorrar(temporal);
            throw;
        }
    }

    public async Task<Stream> AbrirVerificadoAsync(HashSha256 hash, CancellationToken ct)
    {
        var ruta = RutaDe(hash);
        FileStream archivo;
        try
        {
            // FileShare.Read: nadie puede escribir el blob entre la verificación y la entrega.
            archivo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, TamanoBuffer, useAsync: true);
        }
        catch (FileNotFoundException)
        {
            throw Comprometida(hash, "el blob no existe en el almacén");
        }
        catch (DirectoryNotFoundException)
        {
            throw Comprometida(hash, "el blob no existe en el almacén");
        }

        try
        {
            var calculado = HashSha256.DesdeBytes(await SHA256.HashDataAsync(archivo, ct));
            if (calculado != hash)
                throw Comprometida(hash, $"el contenido hashea {calculado}");
            archivo.Position = 0;
            return archivo;
        }
        catch
        {
            await archivo.DisposeAsync();
            throw;
        }
    }

    private IntegridadComprometidaException Comprometida(HashSha256 hash, string motivo)
    {
        logger.LogCritical("Integridad comprometida en el almacén de evidencia: {Hash}: {Motivo}", hash.Hex, motivo);
        return new IntegridadComprometidaException(hash, motivo);
    }

    internal string RutaDe(HashSha256 hash) =>
        Path.Combine(_raiz, hash.Hex[..2], hash.Hex[2..4], hash.Hex);

    private void TryBorrar(string ruta)
    {
        try
        {
            File.Delete(ruta);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo borrar el temporal de ingesta {Ruta}", ruta);
        }
    }
}
