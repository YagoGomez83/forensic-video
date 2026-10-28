using Forensic.Domain.Custodia;
using Microsoft.Extensions.Logging;

namespace Forensic.Application.Custodia;

/// <summary>
/// Clave de firma del laboratorio. La privada es el secreto crítico del sistema: nunca en código ni
/// en imágenes; la carga la infraestructura desde un almacén seguro.
/// </summary>
public interface IFirmanteDeCadena
{
    /// <summary>Identifica la clave pública (<c>ed25519:&lt;sha256 de la pública&gt;</c>).</summary>
    string ClaveId { get; }

    byte[] ClavePublica { get; }

    byte[] Firmar(byte[] mensaje);

    /// <summary>Verifica la firma de un sello contra las claves de confianza del laboratorio.</summary>
    bool Verificar(SelloDeCadena sello);
}

/// <summary>
/// Almacén append-only de sellos, separado de la base: quien pueda reescribir la cadena no debe
/// poder reescribir sus sellos.
/// </summary>
public interface IAlmacenDeSellos
{
    /// <summary>Agrega el sello. Devuelve false, sin tocar nada, si esa secuencia ya estaba sellada.</summary>
    Task<bool> AgregarAsync(SelloDeCadena sello, CancellationToken ct);

    /// <summary>Los sellos del caso, ordenados por secuencia.</summary>
    Task<IReadOnlyList<SelloDeCadena>> ListarAsync(Guid casoId, CancellationToken ct);
}

/// <summary>
/// Sella la cabeza de la cadena de cada caso con el timestamp del servidor y la clave del
/// laboratorio. Lo invoca el sellado periódico; no actúa en nombre de ningún usuario ni escribe en
/// la cadena.
/// </summary>
public sealed class ServicioDeSellado(
    IPersistenciaForense persistencia, IAlmacenDeSellos almacen, IFirmanteDeCadena firmante,
    Identidad.ILectorDeAuditoriaDeAccesos auditoriaDeAccesos, TimeProvider reloj, ILogger<ServicioDeSellado> logger)
{
    /// <summary>
    /// Sella cada caso que tenga asientos posteriores a su último sello. Nunca sella una cadena que
    /// no verifica, ni por sí misma ni contra sus sellos anteriores: sería darle un testigo a una
    /// falsificación. Eso queda como evento de seguridad en el log técnico. Devuelve cuántos sellos agregó.
    /// </summary>
    public async Task<int> SellarPendientesAsync(CancellationToken ct)
    {
        var agregados = 0;
        foreach (var caso in await persistencia.ListarCasosAsync(ct))
        {
            if (await SellarSiHaceFaltaAsync(caso.Id, ct))
                agregados++;
        }
        if (await SellarAuditoriaDeAccesosAsync(ct))
            agregados++;
        return agregados;
    }

    /// <summary>La auditoría de accesos se sella igual, con su propio id: también delata una reconstrucción o un truncado.</summary>
    private async Task<bool> SellarAuditoriaDeAccesosAsync(CancellationToken ct)
    {
        var cadena = await auditoriaDeAccesos.LeerCadenaAsync(ct);
        if (cadena.Count == 0)
            return false;
        var verificacion = Domain.Identidad.CadenaDeAccesos.Verificar(cadena);
        if (!verificacion.EsIntegra)
        {
            logger.LogCritical("No se sella la auditoría de accesos: no verifica en la secuencia {Secuencia}: {Motivo}",
                verificacion.SecuenciaRota, verificacion.Motivo);
            return false;
        }
        var id = Domain.Identidad.CadenaDeAccesos.IdParaSellos;
        var cruce = RegistroDeCustodia.VerificarSellos(
            id, cadena.Select(a => (a.Secuencia, a.Hash)).ToList(), await almacen.ListarAsync(id, ct), firmante.Verificar);
        if (!cruce.SonValidos)
        {
            logger.LogCritical("No se sella la auditoría de accesos: no coincide con el sello de la secuencia {Secuencia}: {Motivo}",
                cruce.SecuenciaRota, cruce.Motivo);
            return false;
        }
        if (cruce.AsientosSinSellar == 0)
            return false;
        var cabeza = cadena[^1];
        return await almacen.AgregarAsync(
            SelloDeCadena.Sellar(id, cabeza.Secuencia, cabeza.Hash, reloj.GetUtcNow(), firmante.ClaveId, firmante.Firmar), ct);
    }

    /// <summary>
    /// Sella esta cabeza si todavía no está sellada. Lo usa la emisión de un informe, sobre una cadena
    /// que ya verificó <see cref="VerificadorDeCadena"/>: el bundle tiene que traer el sello de su cabeza.
    /// </summary>
    public async Task SellarCabezaAsync(AsientoDeCustodia cabeza, CancellationToken ct) =>
        await almacen.AgregarAsync(SelloDeCadena.Sellar(cabeza, reloj.GetUtcNow(), firmante.ClaveId, firmante.Firmar), ct);

    private async Task<bool> SellarSiHaceFaltaAsync(Guid casoId, CancellationToken ct)
    {
        var cadena = await persistencia.ObtenerCadenaAsync(casoId, ct);
        if (cadena.Count == 0)
            return false;

        var verificacion = RegistroDeCustodia.Verificar(casoId, cadena);
        if (!verificacion.EsIntegra)
        {
            logger.LogCritical("No se sella el caso {CasoId}: su cadena no verifica en la secuencia {Secuencia}: {Motivo}",
                casoId, verificacion.SecuenciaRota, verificacion.Motivo);
            return false;
        }
        var sellos = await almacen.ListarAsync(casoId, ct);
        var cruce = RegistroDeCustodia.VerificarSellos(casoId, cadena, sellos, firmante.Verificar);
        if (!cruce.SonValidos)
        {
            logger.LogCritical("No se sella el caso {CasoId}: su cadena no coincide con el sello de la secuencia {Secuencia}: {Motivo}",
                casoId, cruce.SecuenciaRota, cruce.Motivo);
            return false;
        }
        if (cruce.AsientosSinSellar == 0)
            return false;

        var sello = SelloDeCadena.Sellar(cadena[^1], reloj.GetUtcNow(), firmante.ClaveId, firmante.Firmar);
        return await almacen.AgregarAsync(sello, ct);
    }
}

/// <summary>
/// La cadena de un caso en la que se puede confiar para decidir algo pericial (reproducir, emitir un
/// informe): verifica por sí misma y coincide con todos sus sellos. Si no, nada que dependa de ella sigue.
/// </summary>
public sealed class VerificadorDeCadena(IPersistenciaForense persistencia, IAlmacenDeSellos almacen, IFirmanteDeCadena firmante)
{
    public async Task<IReadOnlyList<AsientoDeCustodia>> ObtenerConfiableAsync(Guid casoId, CancellationToken ct)
    {
        var cadena = await persistencia.ObtenerCadenaAsync(casoId, ct);
        var verificacion = RegistroDeCustodia.Verificar(casoId, cadena);
        if (!verificacion.EsIntegra)
            throw new CadenaComprometidaException(
                $"La cadena del caso no verifica (asiento {verificacion.SecuenciaRota}: {verificacion.Motivo}).");

        var cruce = RegistroDeCustodia.VerificarSellos(casoId, cadena, await almacen.ListarAsync(casoId, ct), firmante.Verificar);
        if (!cruce.SonValidos)
            throw new CadenaComprometidaException(
                $"La cadena del caso no coincide con su sello de la secuencia {cruce.SecuenciaRota}: {cruce.Motivo}");
        return cadena;
    }
}
