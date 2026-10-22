using Forensic.Application.Evidencias;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Forensic.Api.Controllers;

public sealed class OpcionesIngesta
{
    public const string Seccion = "Ingesta";

    /// <summary>Tope de tamaño por evidencia. Por defecto 20 GiB (exportaciones de video largas).</summary>
    public long TamanoMaximoBytes { get; set; } = 20L * 1024 * 1024 * 1024;
}

[ApiController]
[Route("casos/{casoId:guid}/evidencias")]
public sealed class EvidenciasController(ServicioDeEvidencias evidencias, IOptions<OpcionesIngesta> opciones) : ControllerBase
{
    /// <summary>
    /// Ingesta de un original. El cuerpo es el archivo en crudo (application/octet-stream) y se
    /// procesa en streaming, sin buffering en memoria. El nombre viaja en la query porque las
    /// cabeceras no admiten caracteres fuera de ASCII.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<EvidenciaDto>> Ingestar(Guid casoId, [FromQuery] string nombre, CancellationToken ct)
    {
        if (HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite)
            limite.MaxRequestBodySize = opciones.Value.TamanoMaximoBytes;

        var evidencia = await evidencias.IngestarAsync(casoId, nombre, Request.Body, ct);
        return Created($"/casos/{casoId}/evidencias/{evidencia.Id}", evidencia);
    }

    [HttpGet]
    public Task<IReadOnlyList<EvidenciaDto>> Listar(Guid casoId, CancellationToken ct) => evidencias.ListarAsync(casoId, ct);

    /// <summary>
    /// Re-ejecuta el control cruzado de una evidencia que se ingestó con el worker caído. El resultado
    /// queda en un asiento nuevo de la cadena.
    /// </summary>
    [HttpPost("{evidenciaId:guid}/control-cruzado")]
    public Task<ResultadoDeControlCruzado> CompletarControlCruzado(Guid casoId, Guid evidenciaId, CancellationToken ct) =>
        evidencias.CompletarControlCruzadoAsync(casoId, evidenciaId, ct);

    /// <summary>Contenido de la evidencia, entregado sólo después de verificar su hash.</summary>
    [HttpGet("{evidenciaId:guid}/contenido")]
    public async Task<IActionResult> Contenido(Guid casoId, Guid evidenciaId, CancellationToken ct)
    {
        var (evidencia, contenido) = await evidencias.AbrirAsync(casoId, evidenciaId, ct);
        Response.Headers["X-Evidencia-Sha256"] = evidencia.Hash;
        return File(contenido, "application/octet-stream", evidencia.NombreOriginal);
    }
}
