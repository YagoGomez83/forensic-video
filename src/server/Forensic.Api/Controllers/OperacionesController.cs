using Forensic.Application.Operaciones;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.Api.Controllers;

[ApiController]
[Route("casos/{casoId:guid}/evidencias/{evidenciaId:guid}")]
public sealed class OperacionesController(ServicioDeOperaciones operaciones) : ControllerBase
{
    /// <summary>
    /// Aplica una operación determinista del catálogo y crea un derivado nuevo, asentado en la cadena.
    /// El original no se toca.
    /// </summary>
    [HttpPost("operaciones")]
    public async Task<ActionResult<OperacionAplicadaDto>> Aplicar(
        Guid casoId, Guid evidenciaId, AplicarOperacionRequest req, CancellationToken ct)
    {
        var aplicada = await operaciones.AplicarAsync(casoId, evidenciaId, req.Tipo, req.Parametros, req.Nombre, ct);
        return Created($"/casos/{casoId}/evidencias/{aplicada.Derivado.Id}", aplicada);
    }

    /// <summary>
    /// Re-ejecuta la operación de un derivado desde su asiento y compara el hash byte a byte. Queda
    /// asentado. Lo puede pedir cualquiera con lectura del caso.
    /// </summary>
    [HttpPost("reproducir")]
    public Task<ResultadoDeReproduccion> Reproducir(Guid casoId, Guid evidenciaId, CancellationToken ct) =>
        operaciones.ReproducirAsync(casoId, evidenciaId, ct);
}
