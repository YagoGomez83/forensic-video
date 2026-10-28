using Forensic.Application.Casos;
using Forensic.Domain.Custodia;

namespace Forensic.Application.Custodia;

public sealed record AsientoDto(
    long Secuencia, Guid Id, string Tipo, Guid? EvidenciaId, string Detalle, string Actor,
    DateTimeOffset Timestamp, string HashAnterior, string Hash);

public sealed record VerificacionDto(bool EsIntegra, int CantidadAsientos, string? HashFinal, long? SecuenciaRota, string? Motivo);

/// <summary>
/// Export del registro de custodia de un caso: todos los asientos con sus hashes y el resultado
/// de verificar la cadena. Base del informe pericial; un tercero puede reverificarlo sin el código
/// siguiendo el formato canónico documentado en <see cref="AsientoDeCustodia"/>.
/// <paramref name="Sellado"/> cruza la cadena con sus sellos: una cadena reconstruida entera verifica
/// en <paramref name="Verificacion"/>, pero no contra un sello anterior.
/// <paramref name="CrucesPendientes"/> cuenta las evidencias cuyo control cruzado de ingesta sigue
/// pendiente: un caso cerrado con pendientes se ve marcado, nunca silenciosamente incompleto.
/// </summary>
public sealed record ExportDeCustodia(
    string Formato, DateTimeOffset GeneradoEn, string GeneradoPor, CasoDto Caso,
    IReadOnlyList<AsientoDto> Asientos, VerificacionDto Verificacion, int CrucesPendientes, SelladoDto Sellado);

public sealed record SelloDto(long Secuencia, string HashCabeza, DateTimeOffset SelladoEn, string ClaveId, string Firma);

public sealed record VerificacionDeSellosDto(
    bool SonValidos, int CantidadSellos, long? UltimaSecuenciaSellada, long AsientosSinSellar, long? SecuenciaRota, string? Motivo);

/// <summary>
/// Los sellos del caso y su cruce con la cadena. <see cref="ClavePublica"/> (hex) va para quien
/// quiera verificar las firmas, pero un tercero debe contrastar <see cref="ClaveDelLaboratorio"/>
/// con la huella que el laboratorio publica por otro medio, nunca confiar en la que trae el export.
/// </summary>
public sealed record SelladoDto(
    string ClaveDelLaboratorio, string ClavePublica, IReadOnlyList<SelloDto> Sellos, VerificacionDeSellosDto Verificacion);

public sealed class ServicioDeCustodia(
    IPersistenciaForense persistencia, IActorActual actorActual, TimeProvider reloj,
    IAlmacenDeSellos almacenDeSellos, IFirmanteDeCadena firmante)
{
    public async Task<ExportDeCustodia> ExportarAsync(Guid casoId, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await persistencia.ObtenerCasoAsync(casoId, ct)
            ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
        caso.ExigirLectura(actor);

        var cadena = await persistencia.ObtenerCadenaAsync(casoId, ct);
        var verificacion = RegistroDeCustodia.Verificar(casoId, cadena);
        var sellos = await almacenDeSellos.ListarAsync(casoId, ct);
        var cruce = RegistroDeCustodia.VerificarSellos(casoId, cadena, sellos, firmante.Verificar);

        return new ExportDeCustodia(
            AsientoDeCustodia.FormatoCanonico,
            reloj.GetUtcNow(),
            actor.Usuario,
            CasoDto.Desde(caso),
            cadena.Select(a => new AsientoDto(
                a.Secuencia, a.Id, a.Tipo.ToString(), a.EvidenciaId, a.Detalle, a.Actor,
                a.Timestamp, a.HashAnterior.Hex, a.Hash.Hex)).ToList(),
            new VerificacionDto(
                verificacion.EsIntegra, verificacion.CantidadAsientos, verificacion.HashFinal?.Hex,
                verificacion.SecuenciaRota, verificacion.Motivo),
            ControlesCruzados.Pendientes(cadena).Count,
            new SelladoDto(
                firmante.ClaveId,
                Convert.ToHexString(firmante.ClavePublica).ToLowerInvariant(),
                sellos.Select(s => new SelloDto(s.Secuencia, s.HashCabeza.Hex, s.SelladoEn, s.ClaveId, Convert.ToBase64String(s.Firma))).ToList(),
                new VerificacionDeSellosDto(
                    cruce.SonValidos, cruce.CantidadSellos, cruce.UltimaSecuenciaSellada, cruce.AsientosSinSellar,
                    cruce.SecuenciaRota, cruce.Motivo)));
    }
}
