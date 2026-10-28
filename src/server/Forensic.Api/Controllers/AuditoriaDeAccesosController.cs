using Forensic.Application.Identidad;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.Api.Controllers;

/// <summary>Verificación de la auditoría de accesos: auditor y admin. El servicio verifica el rol.</summary>
[ApiController]
[Route("auditoria-de-accesos")]
public sealed class AuditoriaDeAccesosController(ServicioDeAuditoriaDeAccesos auditoria) : ControllerBase
{
    [HttpGet("verificacion")]
    public Task<VerificacionDeAccesosDto> Verificar(CancellationToken ct) => auditoria.VerificarAsync(ct);
}
