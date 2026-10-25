using System.Security.Claims;
using System.Text.Encodings.Web;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Identidad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forensic.IntegrationTests;

/// <summary>
/// Esquema de autenticación SÓLO de tests: toma el actor de <c>X-Usuario</c>/<c>X-Rol</c> sin
/// verificar nada, para que los tests de custodia no dependan del login. Vive en el proyecto de
/// tests y lo registra <see cref="ServidorForense"/> (entorno Testing): el servidor real no tiene
/// ningún camino de autenticación por cabeceras. Sin esas cabeceras, la petición va por JWT.
/// </summary>
internal sealed class AutenticacionPorCabeceras(
    IOptionsMonitor<AuthenticationSchemeOptions> opciones, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, logger, encoder)
{
    public const string CabeceraUsuario = "X-Usuario";
    public const string CabeceraRol = "X-Rol";

    private const string Esquema = "Cabeceras";
    private const string CabecerasOJwt = "CabecerasOJwt";

    public static void Registrar(IServiceCollection servicios)
    {
        servicios.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AutenticacionPorCabeceras>(Esquema, null)
            .AddPolicyScheme(CabecerasOJwt, null, o => o.ForwardDefaultSelector = ctx =>
                ctx.Request.Headers.ContainsKey(CabeceraUsuario) ? Esquema : JwtBearerDefaults.AuthenticationScheme);
        servicios.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultScheme = CabecerasOJwt;
            o.DefaultAuthenticateScheme = CabecerasOJwt;
            o.DefaultChallengeScheme = CabecerasOJwt;
            o.DefaultForbidScheme = CabecerasOJwt;
        });
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var usuario = Request.Headers[CabeceraUsuario].ToString();
        var rol = Request.Headers[CabeceraRol].ToString();
        if (string.IsNullOrWhiteSpace(usuario) || !Enum.TryParse<Rol>(rol, ignoreCase: true, out var r) || !Enum.IsDefined(r))
            return Task.FromResult(AuthenticateResult.Fail($"Faltan {CabeceraUsuario} y {CabeceraRol}."));

        var identidad = new ClaimsIdentity(
            [new Claim(OpcionesJwt.ClaimUsuario, usuario), new Claim(OpcionesJwt.ClaimRol, r.ToString())],
            Esquema, OpcionesJwt.ClaimUsuario, OpcionesJwt.ClaimRol);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema)));
    }
}
