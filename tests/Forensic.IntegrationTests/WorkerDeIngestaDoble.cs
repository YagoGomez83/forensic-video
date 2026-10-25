using System.Text.Json;
using Forensic.Application;
using Forensic.Domain.Comun;

namespace Forensic.IntegrationTests;

/// <summary>
/// Doble del worker de ingesta: hashea lo que recibe. Con <paramref name="discrepar"/> reporta el
/// hash de otros bytes (mismo tamaño), como haría un worker que vio un contenido distinto.
/// </summary>
internal sealed class WorkerDeIngestaDoble(bool discrepar = false, string? motivoSinMetadatos = null) : IWorkerDeIngesta
{
    public static readonly JsonElement Metadatos =
        JsonDocument.Parse("""{"version":"doble","formato":{"format_name":"mov,mp4,m4a"},"streams":[]}""").RootElement;

    public async Task<AnalisisDeIngesta> AnalizarAsync(Stream contenido, CancellationToken ct)
    {
        using var copia = new MemoryStream();
        await contenido.CopyToAsync(copia, ct);
        var bytes = copia.ToArray();
        if (discrepar)
            bytes[0] ^= 0xFF;

        return motivoSinMetadatos is null
            ? new AnalisisDeIngesta(HashSha256.Calcular(bytes), bytes.Length, Metadatos, null)
            : new AnalisisDeIngesta(HashSha256.Calcular(bytes), bytes.Length, null, motivoSinMetadatos);
    }
}
