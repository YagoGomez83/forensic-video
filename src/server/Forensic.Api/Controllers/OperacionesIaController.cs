using Forensic.Application.Ia;
using Forensic.Application.Operaciones;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.Api.Controllers;

[ApiController]
[Route("casos/{casoId:guid}/evidencias/{evidenciaId:guid}/operaciones-ia")]
public sealed class OperacionesIaController(ServicioDeOperacionesIa operaciones) : ControllerBase
{
    /// <summary>Tope del derivado: una super-resolución x4 de 1920 x 1080 queda bastante por debajo.</summary>
    public const long TamanoMaximo = 512L * 1024 * 1024;

    /// <summary>
    /// Registra una operación de IA hecha en la workstation: el PNG que devolvió el worker y su reporte
    /// (cabecera X-Reporte), tal cual. El derivado es siempre «sólo investigación».
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(TamanoMaximo)]
    [RequestFormLimits(MultipartBodyLengthLimit = TamanoMaximo)]
    public async Task<ActionResult<OperacionAplicadaDto>> Registrar(
        Guid casoId, Guid evidenciaId, [FromForm] string reporte, IFormFile salida, [FromForm] string? nombre,
        CancellationToken ct)
    {
        await using var contenido = salida.OpenReadStream();
        var aplicada = await operaciones.RegistrarAsync(casoId, evidenciaId, reporte, contenido, nombre, ct);
        return Created($"/casos/{casoId}/evidencias/{aplicada.Derivado.Id}", aplicada);
    }
}
