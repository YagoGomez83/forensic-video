using Forensic.Api.Identidad;
using Forensic.Application.Identidad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(ServicioDeIdentidad identidad) : ControllerBase
{
    /// <summary>
    /// Login con usuario y contraseña. Cualquier rechazo es el mismo 401 genérico; el motivo queda
    /// en la auditoría de accesos.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public Task<TokenEmitido> Login(LoginRequest req, CancellationToken ct) =>
        identidad.AutenticarAsync(req.Usuario, req.Contrasena, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

    /// <summary>
    /// Cambio de la propia contraseña. Es lo único que permite una contraseña inicial o reseteada.
    /// Devuelve un token nuevo.
    /// </summary>
    [HttpPost("contrasena")]
    [Authorize(Policy = ValidacionDeSesion.CambioDeContrasena)]
    public Task<TokenEmitido> CambiarContrasena(CambioDeContrasenaRequest req, CancellationToken ct) =>
        identidad.CambiarContrasenaAsync(req.Actual, req.Nueva, ct);
}
