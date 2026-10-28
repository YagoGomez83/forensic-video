using Forensic.Domain.Custodia;

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
