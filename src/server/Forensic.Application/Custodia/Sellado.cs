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
    TimeProvider reloj, ILogger<ServicioDeSellado> logger)
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
        return agregados;
    }

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
