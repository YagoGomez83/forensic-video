using Forensic.Application.Custodia;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Identidad;

namespace Forensic.Application.Identidad;

/// <summary>Lee la auditoría de accesos con su encadenado, ordenada por secuencia.</summary>
public interface ILectorDeAuditoriaDeAccesos
{
    Task<IReadOnlyList<AccesoEncadenado>> LeerCadenaAsync(CancellationToken ct);
}

/// <summary>La auditoría de accesos verificada por sí misma y contra sus sellos.</summary>
public sealed record VerificacionDeAccesosDto(VerificacionDto Verificacion, VerificacionDeSellosDto Sellado);

/// <summary>
/// Verifica la auditoría de accesos: recalcula cada hash (la base los calculó al insertar) y la cruza
/// con sus sellos. La ven el auditor y el admin; nadie que trabaje casos.
/// </summary>
public sealed class ServicioDeAuditoriaDeAccesos(
    ILectorDeAuditoriaDeAccesos lector, IAlmacenDeSellos almacenDeSellos, IFirmanteDeCadena firmante, IActorActual actorActual)
{
    public async Task<VerificacionDeAccesosDto> VerificarAsync(CancellationToken ct)
    {
        if (actorActual.Actor.Rol is not (Rol.Auditor or Rol.Admin))
            throw new AccesoDenegadoException("Sólo un auditor o un admin verifican la auditoría de accesos.");

        var cadena = await lector.LeerCadenaAsync(ct);
        var v = CadenaDeAccesos.Verificar(cadena);
        var sellos = await almacenDeSellos.ListarAsync(CadenaDeAccesos.IdParaSellos, ct);
        var cruce = RegistroDeCustodia.VerificarSellos(
            CadenaDeAccesos.IdParaSellos, cadena.Select(a => (a.Secuencia, a.Hash)).ToList(), sellos, firmante.Verificar);
        return new VerificacionDeAccesosDto(
            new VerificacionDto(v.EsIntegra, v.CantidadAsientos, v.HashFinal?.Hex, v.SecuenciaRota, v.Motivo),
            new VerificacionDeSellosDto(cruce.SonValidos, cruce.CantidadSellos, cruce.UltimaSecuenciaSellada, cruce.AsientosSinSellar,
                cruce.SecuenciaRota, cruce.Motivo));
    }
}
