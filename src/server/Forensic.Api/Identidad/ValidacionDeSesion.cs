using System.Security.Claims;
using Forensic.Application.Identidad;
using Forensic.Infrastructure.Identidad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Forensic.Api.Identidad;

/// <summary>
/// Un token bien firmado no alcanza: en cada petición se contrasta con el estado vigente del usuario.
/// Si está deshabilitado o su rol cambió desde que se emitió el token, la petición es 401; deshabilitar
/// o cambiar un rol rige al instante y no al vencer el token.
/// </summary>
internal static class ValidacionDeSesion
{
    /// <summary>Lo agrega el servidor (nunca viene en el token) si el usuario debe cambiar su contraseña.</summary>
    public const string ClaimDebeCambiarContrasena = "debe_cambiar_contrasena";

    /// <summary>Sesión completa: autenticada y sin contraseña temporal pendiente de cambio.</summary>
    public const string SesionCompleta = "SesionCompleta";

    /// <summary>Basta estar autenticado: es la política del cambio de contraseña.</summary>
    public const string CambioDeContrasena = "CambioDeContrasena";

    public const string SoloAdmin = "SoloAdmin";

    public static async Task ValidarAsync(TokenValidatedContext contexto)
    {
        var principal = contexto.Principal;
        var nombre = principal?.FindFirst(OpcionesJwt.ClaimUsuario)?.Value;
        var rol = principal?.FindFirst(OpcionesJwt.ClaimRol)?.Value;
        if (principal?.Identity is not ClaimsIdentity identidad || string.IsNullOrEmpty(nombre))
        {
            contexto.Fail("Token sin usuario.");
            return;
        }

        var servicio = contexto.HttpContext.RequestServices.GetRequiredService<ServicioDeIdentidad>();
        var estado = await servicio.ObtenerEstadoDeSesionAsync(nombre, contexto.HttpContext.RequestAborted);
        if (estado is null || estado.Rol.ToString() != rol)
        {
            contexto.Fail("La sesión ya no es vigente: el usuario fue deshabilitado o cambió su rol.");
            return;
        }
        if (estado.DebeCambiarContrasena)
            identidad.AddClaim(new Claim(ClaimDebeCambiarContrasena, "true"));
    }

    public static void Configurar(AuthorizationOptions opciones)
    {
        var completa = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(c => !c.User.HasClaim(ClaimDebeCambiarContrasena, "true"))
            .Build();
        // [Authorize] por defecto: todo endpoint sin atributos exige sesión completa.
        opciones.DefaultPolicy = completa;
        opciones.FallbackPolicy = completa;
        opciones.AddPolicy(SesionCompleta, completa);
        opciones.AddPolicy(CambioDeContrasena, p => p.RequireAuthenticatedUser());
        opciones.AddPolicy(SoloAdmin, p => p.Combine(completa).RequireRole(nameof(Domain.Comun.Rol.Admin)));
    }
}
