using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Forensic.IntegrationTests;

/// <summary>
/// Una CA de laboratorio de prueba y sus certificados, generados en memoria en cada corrida. Los reales
/// se generan fuera del repo (scripts/generar-certificados-mtls.sh).
/// </summary>
internal sealed class CertificadosDePrueba
{
    private static readonly Oid ServidorTls = new("1.3.6.1.5.5.7.3.1");
    private static readonly Oid ClienteTls = new("1.3.6.1.5.5.7.3.2");

    public CertificadosDePrueba(string nombre = "CA de prueba del laboratorio")
    {
        using var clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={nombre}", clave, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        Ca = Persistible(req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddYears(1)));
    }

    public X509Certificate2 Ca { get; }

    /// <summary>La CA sin su clave privada, como la recibe el servidor o el cliente.</summary>
    public X509Certificate2 CaPublica => new(Ca.Export(X509ContentType.Cert));

    public X509Certificate2 Servidor(string host = "localhost") => Emitir($"CN={host}", ServidorTls, san: host);

    public X509Certificate2 Cliente(string workstation = "workstation-01", DateTimeOffset? vence = null) =>
        Emitir($"CN={workstation}", ClienteTls, san: null, vence);

    private X509Certificate2 Emitir(string sujeto, Oid uso, string? san, DateTimeOffset? vence = null)
    {
        using var clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(sujeto, clave, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([uso], true));
        if (san is not null)
        {
            var nombres = new SubjectAlternativeNameBuilder();
            nombres.AddDnsName(san);
            if (san == "localhost")
                nombres.AddIpAddress(IPAddress.Loopback);
            req.CertificateExtensions.Add(nombres.Build());
        }
        var hasta = vence ?? DateTimeOffset.UtcNow.AddDays(30);
        var desde = hasta < DateTimeOffset.UtcNow ? hasta.AddDays(-30) : DateTimeOffset.UtcNow.AddDays(-1);
        using var emitido = req.Create(Ca, desde, hasta, RandomNumberGenerator.GetBytes(16));
        return Persistible(emitido.CopyWithPrivateKey(clave));
    }

    /// <summary>
    /// Una CRL firmada por esta CA, en DER, con los certificados revocados que se le pasen. La genera el
    /// CertificateRevocationListBuilder de .NET; el servidor la lee con su propio parser.
    /// </summary>
    public byte[] Crl(IEnumerable<X509Certificate2>? revocados = null, DateTimeOffset? proxima = null, DateTimeOffset? emitida = null)
    {
        var crl = new CertificateRevocationListBuilder();
        foreach (var revocado in revocados ?? [])
            crl.AddEntry(revocado, DateTimeOffset.UtcNow.AddMinutes(-5));
        var hasta = proxima ?? DateTimeOffset.UtcNow.AddDays(365);
        var desde = emitida ?? (hasta < DateTimeOffset.UtcNow ? hasta.AddDays(-30) : DateTimeOffset.UtcNow.AddMinutes(-5));
        return crl.Build(Ca, BigInteger.One, hasta, HashAlgorithmName.SHA256, thisUpdate: desde);
    }

    public static string CrlEnPem(byte[] der) => new(PemEncoding.Write("X509 CRL", der));

    /// <summary>
    /// En Windows, SslStream no usa claves efímeras: el certificado se re-importa desde un PKCS#12 para que
    /// su clave quede en un contenedor utilizable.
    /// </summary>
    public static X509Certificate2 Persistible(X509Certificate2 cert) =>
        new(cert.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.Exportable);

    /// <summary>Escribe el certificado y su clave en PEM, como los montaría un Docker secret.</summary>
    public static (string Certificado, string Clave) EnPem(X509Certificate2 cert, string carpeta, string nombre)
    {
        Directory.CreateDirectory(carpeta);
        var rutaCert = Path.Combine(carpeta, nombre + ".crt");
        var rutaClave = Path.Combine(carpeta, nombre + ".key");
        File.WriteAllText(rutaCert, cert.ExportCertificatePem());
        if (cert.HasPrivateKey)
        {
            // Una clave CNG de Windows no se exporta en claro: se exporta cifrada y se re-importa en memoria.
            var parametros = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1);
            var cifrada = cert.GetECDsaPrivateKey()!.ExportEncryptedPkcs8PrivateKey("tmp", parametros);
            using var enMemoria = ECDsa.Create();
            enMemoria.ImportEncryptedPkcs8PrivateKey("tmp", cifrada, out _);
            File.WriteAllText(rutaClave, enMemoria.ExportPkcs8PrivateKeyPem());
        }
        return (rutaCert, rutaClave);
    }
}
