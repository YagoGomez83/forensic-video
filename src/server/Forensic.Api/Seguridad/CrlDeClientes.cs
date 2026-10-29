using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Forensic.Api.Seguridad;

/// <summary>
/// La lista de certificados revocados (CRL, RFC 5280) que emite la CA del laboratorio, leída sin red.
/// .NET no permite darle a X509Chain una CRL propia en todas las plataformas, así que se lee acá: sólo
/// cuenta si la firmó la CA, si es una CRL completa (nada de delta ni de otro emisor) y mientras esté
/// vigente.
/// </summary>
public sealed class CrlDeClientes
{
    /// <summary>Cuánto antes de vencer empieza a avisar en el log.</summary>
    public static readonly TimeSpan Aviso = TimeSpan.FromDays(30);

    private static readonly Dictionary<string, (HashAlgorithmName Hash, bool Ecdsa)> Firmas = new()
    {
        ["1.2.840.10045.4.3.2"] = (HashAlgorithmName.SHA256, true),
        ["1.2.840.10045.4.3.3"] = (HashAlgorithmName.SHA384, true),
        ["1.2.840.10045.4.3.4"] = (HashAlgorithmName.SHA512, true),
        ["1.2.840.113549.1.1.11"] = (HashAlgorithmName.SHA256, false),
        ["1.2.840.113549.1.1.12"] = (HashAlgorithmName.SHA384, false),
        ["1.2.840.113549.1.1.13"] = (HashAlgorithmName.SHA512, false),
    };

    private readonly HashSet<string> _revocados;

    private CrlDeClientes(DateTimeOffset emitidaEn, DateTimeOffset proximaActualizacion, HashSet<string> revocados)
    {
        EmitidaEn = emitidaEn;
        ProximaActualizacion = proximaActualizacion;
        _revocados = revocados;
    }

    public DateTimeOffset EmitidaEn { get; }

    public DateTimeOffset ProximaActualizacion { get; }

    public int CantidadDeRevocados => _revocados.Count;

    /// <summary>Lee una CRL en PEM o DER y la valida contra la CA. Si no la acepta, lanza InvalidOperationException.</summary>
    public static CrlDeClientes Cargar(byte[] contenido, X509Certificate2 ca)
    {
        try
        {
            return Leer(Der(contenido), ca);
        }
        catch (Exception ex) when (ex is AsnContentException or CryptographicException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"La CRL de clientes no es una CRL válida: {ex.Message}", ex);
        }
    }

    public bool EstaRevocado(X509Certificate2 certificado) => _revocados.Contains(Serie(certificado.SerialNumberBytes.Span));

    public bool Vencida(DateTimeOffset ahora) => ahora >= ProximaActualizacion;

    public bool PorVencer(DateTimeOffset ahora) => ahora >= ProximaActualizacion - Aviso;

    private static byte[] Der(byte[] contenido)
    {
        var texto = Encoding.ASCII.GetString(contenido);
        if (!texto.Contains("-----BEGIN", StringComparison.Ordinal))
            return contenido;
        if (!PemEncoding.TryFind(texto, out var pem) || texto[pem.Label] != "X509 CRL")
            throw new FormatException("el PEM no es una «X509 CRL».");
        return Convert.FromBase64String(texto[pem.Base64Data]);
    }

    private static CrlDeClientes Leer(byte[] der, X509Certificate2 ca)
    {
        // CertificateList ::= SEQUENCE { tbsCertList, signatureAlgorithm, signatureValue BIT STRING }
        var lector = new AsnReader(der, AsnEncodingRules.DER);
        var lista = lector.ReadSequence();
        lector.ThrowIfNotEmpty();
        var tbs = lista.ReadEncodedValue().ToArray();
        var algoritmo = LeerAlgoritmo(lista);
        var firma = lista.ReadBitString(out var bitsSobrantes);
        lista.ThrowIfNotEmpty();
        if (bitsSobrantes != 0)
            throw new FormatException("la firma no ocupa bytes enteros.");

        var cuerpo = new AsnReader(tbs, AsnEncodingRules.DER).ReadSequence();
        if (cuerpo.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
        {
            if (!cuerpo.TryReadInt32(out var version) || version != 1)
                throw new FormatException("sólo se admite la versión 2 de CRL.");
        }
        if (LeerAlgoritmo(cuerpo) != algoritmo)
            throw new FormatException("el algoritmo de firma no coincide entre el cuerpo y la firma.");
        var emisor = cuerpo.ReadEncodedValue();
        if (!emisor.Span.SequenceEqual(ca.SubjectName.RawData))
            throw new InvalidOperationException($"La CRL de clientes no es de la CA del laboratorio: su emisor es otro ({new X500DistinguishedName(emisor.ToArray()).Name}).");

        // La firma, antes de creer nada de lo que dice.
        if (!FirmaValida(ca, algoritmo, tbs, firma))
            throw new InvalidOperationException("La CRL de clientes no tiene una firma válida de la CA del laboratorio.");

        var emitidaEn = LeerFecha(cuerpo);
        // Sin nextUpdate no hay forma de saber si sigue vigente.
        if (!cuerpo.HasData || !EsFecha(cuerpo.PeekTag()))
            throw new FormatException("la CRL no tiene fecha de próxima actualización (nextUpdate).");
        var proxima = LeerFecha(cuerpo);

        var revocados = new HashSet<string>(StringComparer.Ordinal);
        if (cuerpo.HasData && cuerpo.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
        {
            var entradas = cuerpo.ReadSequence();
            while (entradas.HasData)
            {
                var entrada = entradas.ReadSequence();
                revocados.Add(Serie(entrada.ReadIntegerBytes().Span));
                LeerFecha(entrada);
                if (entrada.HasData)
                    RechazarCriticas(entrada.ReadSequence(), "de una entrada");
                entrada.ThrowIfNotEmpty();
            }
        }
        if (cuerpo.HasData)
        {
            var extensiones = cuerpo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
            RechazarCriticas(extensiones.ReadSequence(), "de la CRL");
            extensiones.ThrowIfNotEmpty();
        }
        cuerpo.ThrowIfNotEmpty();
        return new CrlDeClientes(emitidaEn, proxima, revocados);
    }

    private static bool EsFecha(Asn1Tag tag) =>
        tag.HasSameClassAndValue(Asn1Tag.UtcTime) || tag.HasSameClassAndValue(Asn1Tag.GeneralizedTime);

    private static string LeerAlgoritmo(AsnReader lector)
    {
        var algoritmo = lector.ReadSequence();
        var oid = algoritmo.ReadObjectIdentifier();
        if (algoritmo.HasData)
            algoritmo.ReadNull();
        algoritmo.ThrowIfNotEmpty();
        return oid;
    }

    private static DateTimeOffset LeerFecha(AsnReader lector) =>
        lector.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? lector.ReadUtcTime(2049) : lector.ReadGeneralizedTime();

    /// <summary>
    /// Una extensión crítica que no se entiende obliga a rechazar la CRL (RFC 5280, 5.2 y 5.3): un
    /// punto de distribución parcial, una delta o una CRL indirecta no son la lista completa de la CA.
    /// </summary>
    private static void RechazarCriticas(AsnReader extensiones, string deQue)
    {
        while (extensiones.HasData)
        {
            var extension = extensiones.ReadSequence();
            var oid = extension.ReadObjectIdentifier();
            var critica = extension.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean) && extension.ReadBoolean();
            extension.ReadOctetString();
            extension.ThrowIfNotEmpty();
            if (critica)
                throw new InvalidOperationException($"La CRL de clientes tiene una extensión crítica {deQue} que no se admite ({oid}): tiene que ser la CRL completa de la CA.");
        }
    }

    private static bool FirmaValida(X509Certificate2 ca, string algoritmo, byte[] tbs, byte[] firma)
    {
        if (!Firmas.TryGetValue(algoritmo, out var tipo))
            throw new InvalidOperationException($"La CRL de clientes está firmada con un algoritmo que no se admite ({algoritmo}).");
        if (tipo.Ecdsa)
        {
            using var ecdsa = ca.GetECDsaPublicKey();
            return ecdsa is not null && ecdsa.VerifyData(tbs, firma, tipo.Hash, DSASignatureFormat.Rfc3279DerSequence);
        }
        using var rsa = ca.GetRSAPublicKey();
        return rsa is not null && rsa.VerifyData(tbs, firma, tipo.Hash, RSASignaturePadding.Pkcs1);
    }

    /// <summary>El número de serie sin ceros a la izquierda, en hexadecimal: igual en el certificado y en la CRL.</summary>
    private static string Serie(ReadOnlySpan<byte> bytes)
    {
        var inicio = 0;
        while (inicio < bytes.Length - 1 && bytes[inicio] == 0)
            inicio++;
        return Convert.ToHexString(bytes[inicio..]);
    }
}
