using Forensic.Api.Identidad;
using Forensic.Application.Identidad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.Api.Controllers;

/// <summary>Administración de usuarios: sólo admin. El servicio vuelve a verificar el rol.</summary>
[ApiController]
[Route("usuarios")]
[Authorize(Policy = ValidacionDeSesion.SoloAdmin)]
public sealed class UsuariosController(ServicioDeIdentidad identidad) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UsuarioDto>> Listar(CancellationToken ct) => identidad.ListarAsync(ct);

    [HttpPost]
    public async Task<ActionResult<UsuarioDto>> Crear(CrearUsuarioRequest req, CancellationToken ct)
    {
        var usuario = await identidad.CrearAsync(req.Usuario, req.Rol, req.ContrasenaInicial, ct);
        return Created($"/usuarios/{usuario.Nombre}", usuario);
    }

    [HttpPut("{nombre}/rol")]
    public Task<UsuarioDto> CambiarRol(string nombre, CambiarRolRequest req, CancellationToken ct) =>
        identidad.CambiarRolAsync(nombre, req.Rol, ct);

    [HttpPost("{nombre}/reset-contrasena")]
    public Task<UsuarioDto> ResetearContrasena(string nombre, ResetearContrasenaRequest req, CancellationToken ct) =>
        identidad.ResetearContrasenaAsync(nombre, req.ContrasenaTemporal, ct);

    [HttpPost("{nombre}/deshabilitar")]
    public Task<UsuarioDto> Deshabilitar(string nombre, CancellationToken ct) => identidad.DeshabilitarAsync(nombre, ct);
}
