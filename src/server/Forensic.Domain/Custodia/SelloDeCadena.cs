using System.Globalization;
using System.Text;
using Forensic.Domain.Comun;

namespace Forensic.Domain.Custodia;

/// <summary>
/// Firma del laboratorio sobre el hash de un asiento: «la cadena de este caso, hasta esta secuencia,
/// terminaba en este hash». Vive fuera de la base, en un almacén append-only aparte, así que quien
/// reescriba la cadena entera (recalculando todos sus hashes) no puede reproducir un sello anterior
/// sin la clave privada. También delata el truncado del final, que la cadena sola no ve.
/// </summary>
/// <remarks>
/// El dominio no conoce el algoritmo de firma: firma y verifica bytes a través de delegados. El
/// mensaje firmado es <see cref="MensajeCanonico"/>, con el mismo formato de campos prefijados por
/// longitud que <see cref="AsientoDeCustodia.CalcularHash"/>.
/// </remarks>
public sealed class SelloDeCadena
{
    /// <summary>Versión del mensaje firmado. Cambiarlo exige versionar.</summary>
    public const string FormatoCanonico = "forensic-video/sello/v1";

    private SelloDeCadena(Guid casoId, long secuencia, HashSha256 hashCabeza, DateTimeOffset selladoEn, string claveId, byte[] firma)
    {
        CasoId = casoId;
        Secuencia = secuencia;
        HashCabeza = hashCabeza;
        SelladoEn = selladoEn;
        ClaveId = claveId;
        Firma = firma;
    }

    public Guid CasoId { get; }

    /// <summary>Secuencia del asiento sellado.</summary>
    public long Secuencia { get; }

    /// <summary>Hash del asiento sellado.</summary>
    public HashSha256 HashCabeza { get; }

    /// <summary>Timestamp del servidor, UTC con precisión de microsegundos.</summary>
    public DateTimeOffset SelladoEn { get; }

    /// <summary>Identifica la clave pública con la que verifica la firma.</summary>
    public string ClaveId { get; }

    public byte[] Firma { get; }

    public static SelloDeCadena Sellar(AsientoDeCustodia cabeza, DateTimeOffset ahora, string claveId, Func<byte[], byte[]> firmar) =>
        Sellar(cabeza.CasoId, cabeza.Secuencia, cabeza.Hash, ahora, claveId, firmar);

    /// <summary>
    /// Sella el eslabón de cualquier cadena encadenada por hash: la de un caso (su id) o la auditoría de
    /// accesos (<c>CadenaDeAccesos.IdParaSellos</c>).
    /// </summary>
    public static SelloDeCadena Sellar(
        Guid idCadena, long secuencia, HashSha256 hash, DateTimeOffset ahora, string claveId, Func<byte[], byte[]> firmar)
    {
        if (string.IsNullOrWhiteSpace(claveId))
            throw new ExcepcionDeDominio("Un sello debe identificar su clave.");

        var sello = new SelloDeCadena(idCadena, secuencia, hash, Tiempo.NormalizarParaCustodia(ahora), claveId, []);
        var firma = firmar(sello.MensajeCanonico());
        if (firma is null || firma.Length == 0)
            throw new ExcepcionDeDominio("Sin firma no hay sello.");

        return new SelloDeCadena(sello.CasoId, sello.Secuencia, sello.HashCabeza, sello.SelladoEn, claveId, firma);
    }

    /// <summary>Reconstruye un sello leído del almacén. No verifica nada: eso es <see cref="RegistroDeCustodia.VerificarSellos"/>.</summary>
    public static SelloDeCadena Rehidratar(
        Guid casoId, long secuencia, HashSha256 hashCabeza, DateTimeOffset selladoEn, string claveId, byte[] firma) =>
        new(casoId, secuencia, hashCabeza, selladoEn, claveId, firma);

    /// <summary>Los bytes que se firman: todos los campos salvo la firma.</summary>
    public byte[] MensajeCanonico()
    {
        var sb = new StringBuilder();
        AsientoDeCustodia.Campo(sb, "formato", FormatoCanonico);
        AsientoDeCustodia.Campo(sb, "caso", CasoId.ToString("D"));
        AsientoDeCustodia.Campo(sb, "secuencia", Secuencia.ToString(CultureInfo.InvariantCulture));
        AsientoDeCustodia.Campo(sb, "cabeza", HashCabeza.Hex);
        AsientoDeCustodia.Campo(sb, "sellado", AsientoDeCustodia.TimestampCanonico(SelladoEn));
        AsientoDeCustodia.Campo(sb, "clave", ClaveId);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}

/// <summary>
/// Resultado de cruzar una cadena con sus sellos. <see cref="AsientosSinSellar"/> no es una falla:
/// son los asientos posteriores al último sello, que todavía no tienen testigo externo.
/// </summary>
public sealed record VerificacionDeSellos(
    bool SonValidos, int CantidadSellos, long? UltimaSecuenciaSellada, long AsientosSinSellar, long? SecuenciaRota, string? Motivo);
