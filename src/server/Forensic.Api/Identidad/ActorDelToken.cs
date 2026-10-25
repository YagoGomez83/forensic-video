using Forensic.Application;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Identidad;

namespace Forensic.Api.Identidad;

public sealed class NoAutenticadoException(string mensaje) : Exception(mensaje);

/// <summary>
/// El actor de la petición, tomado del principal que autenticó el esquema JWT (y que
/// <see cref="ValidacionDeSesion"/> ya contrastó con la base). Nunca de datos que mande el cliente.
/// </summary>
internal sealed class ActorDelToken(IHttpContextAccessor accesor) : IActorActual
{
    private Actor? _actor;

    public Actor Actor => _actor ??= Resolver();

    private Actor Resolver()
    {
        var usuario = accesor.HttpContext?.User;
        if (usuario?.Identity is not { IsAuthenticated: true })
            throw new NoAutenticadoException("La petición no está autenticada.");

        var nombre = usuario.FindFirst(OpcionesJwt.ClaimUsuario)?.Value;
        var rol = usuario.FindFirst(OpcionesJwt.ClaimRol)?.Value;
        if (string.IsNullOrWhiteSpace(nombre) || !Enum.TryParse<Rol>(rol, out var r) || !Enum.IsDefined(r))
            throw new NoAutenticadoException("El token no identifica un usuario con rol válido.");
        return new Actor(nombre, r);
    }
}
