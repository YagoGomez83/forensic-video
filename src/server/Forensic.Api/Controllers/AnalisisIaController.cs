using Forensic.Application.Ia;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.Api.Controllers;

[ApiController]
[Route("casos/{casoId:guid}/evidencias/{evidenciaId:guid}/analisis-ia")]
public sealed class AnalisisIaController(ServicioDeAnalisisIa analisis) : ControllerBase
{
    /// <summary>Tope del reporte: viene de la workstation y su JSON crudo va al asiento.</summary>
    public const long TamanoMaximoDelReporte = 4 * 1024 * 1024;

    /// <summary>
    /// Registra un análisis de IA hecho en la workstation (el JSON del worker, sin tocar). Siempre
    /// «sólo investigación»: los hallazgos son pistas, nunca prueba.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(TamanoMaximoDelReporte)]
    public async Task<ActionResult<AnalisisIaDto>> Registrar(
        Guid casoId, Guid evidenciaId, ReporteDeAnalisisIa reporte, CancellationToken ct)
    {
        var registrado = await analisis.RegistrarAsync(casoId, evidenciaId, reporte, ct);
        return Created($"/casos/{casoId}/evidencias/{evidenciaId}/analisis-ia", registrado);
    }

    [HttpGet]
    public Task<IReadOnlyList<AnalisisIaDto>> Listar(Guid casoId, Guid evidenciaId, CancellationToken ct) =>
        analisis.ListarAsync(casoId, evidenciaId, ct);
}
