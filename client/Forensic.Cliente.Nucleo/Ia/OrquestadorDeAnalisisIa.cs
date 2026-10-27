using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Ia;

/// <summary>
/// Orquesta un análisis de IA en la workstation: el worker local analiza la evidencia que el servidor
/// ya entregó verificada, y su respuesta va al servidor sin tocar. El cliente no decide nada: el
/// servidor cruza el hash, valida el modelo, lo marca «sólo investigación» y lo asienta. Si el worker
/// falla, no se reporta nada.
/// </summary>
public sealed class OrquestadorDeAnalisisIa(ClienteForense servidor, ClienteWorkerIa worker)
{
    public async Task<AnalisisIaDto> DetectarAsync(
        Guid casoId, Guid evidenciaId, byte[] contenido, int umbralConfianzaMilesimas, CancellationToken ct)
    {
        var reporte = await worker.DetectarAsync(contenido, umbralConfianzaMilesimas, ct);
        return await servidor.RegistrarAnalisisIaAsync(casoId, evidenciaId, reporte, ct);
    }

    public async Task<AnalisisIaDto> LeerTextoAsync(
        Guid casoId, Guid evidenciaId, byte[] contenido, int umbralConfianzaMilesimas, CancellationToken ct)
    {
        var reporte = await worker.LeerTextoAsync(contenido, umbralConfianzaMilesimas, ct);
        return await servidor.RegistrarAnalisisIaAsync(casoId, evidenciaId, reporte, ct);
    }

    /// <summary>Super-resolución local: el derivado y su reporte van al servidor, que crea el derivado.</summary>
    public async Task<OperacionAplicadaDto> SuperResolverAsync(
        Guid casoId, Guid evidenciaId, byte[] contenido, string? nombre, CancellationToken ct)
    {
        var (png, reporte) = await worker.SuperResolverAsync(contenido, ct);
        return await servidor.RegistrarOperacionIaAsync(casoId, evidenciaId, reporte, png, nombre, ct);
    }
}
