using System.Net;

namespace Forensic.Cliente.Api;

/// <summary>
/// Sube un stream en bloques informando cuántos bytes salieron, y corta entre bloques si se cancela.
/// Lee directo del origen: la evidencia no pasa por ningún archivo temporal.
/// </summary>
internal sealed class ContenidoConProgreso(Stream origen, long longitud, Action<long>? progreso) : HttpContent
{
    private const int TamanoDeBloque = 64 * 1024;

    protected override Task SerializeToStreamAsync(Stream destino, TransportContext? contexto) =>
        SerializeToStreamAsync(destino, contexto, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream destino, TransportContext? contexto, CancellationToken ct)
    {
        var bloque = new byte[TamanoDeBloque];
        long enviados = 0;
        int leidos;
        while ((leidos = await origen.ReadAsync(bloque, ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            await destino.WriteAsync(bloque.AsMemory(0, leidos), ct);
            enviados += leidos;
            progreso?.Invoke(enviados);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = longitud;
        return true;
    }
}
