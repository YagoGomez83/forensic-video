using Forensic.Application;
using Forensic.Domain.Comun;

namespace Forensic.Api.Identidad;

public sealed class NoAutenticadoException(string mensaje) : Exception(mensaje);

/// <summary>
/// PROVISORIO, sólo Development/Testing: toma el actor de las cabeceras <c>X-Usuario</c> y <c>X-Rol</c>
/// sin autenticar. Lo reemplaza el módulo de identidad propio (usuarios, roles, Argon2/bcrypt),
/// todavía pendiente. Program.cs impide arrancar con esto en cualquier otro entorno.
/// </summary>
internal sealed class ActorDeDesarrollo(IHttpContextAccessor accesor) : IActorActual
{
    public const string CabeceraUsuario = "X-Usuario";
    public const string CabeceraRol = "X-Rol";

    private Actor? _actor;

    public Actor Actor => _actor ??= Resolver();

    private Actor Resolver()
    {
        var request = accesor.HttpContext?.Request
            ?? throw new NoAutenticadoException("No hay petición en curso.");
        var usuario = request.Headers[CabeceraUsuario].ToString();
        var rol = request.Headers[CabeceraRol].ToString();

        if (string.IsNullOrWhiteSpace(usuario) || !Enum.TryParse<Rol>(rol, ignoreCase: true, out var r) || !Enum.IsDefined(r))
            throw new NoAutenticadoException(
                $"Faltan las cabeceras {CabeceraUsuario} y {CabeceraRol} (Analista, Supervisor o Auditor).");
        return new Actor(usuario, r);
    }
}
