using System.Security.Cryptography;
using Forensic.Application.Custodia;
using Forensic.Domain.Custodia;
using Sodium;

namespace Forensic.Infrastructure.Sellado;

/// <summary>
/// Ed25519 de libsodium (<c>crypto_sign_detached</c>) con la clave del laboratorio, que llega en
/// PKCS#8 PEM, el formato de <c>openssl genpkey -algorithm ed25519</c>. Ninguna criptografía propia:
/// este adaptador sólo lee la clave y traduce tipos.
/// </summary>
internal sealed class FirmanteEd25519 : IFirmanteDeCadena
{
    // PKCS#8 de una clave Ed25519 (RFC 8410): esta cabecera DER fija y la semilla de 32 bytes.
    private static readonly byte[] PrefijoPkcs8 = Convert.FromHexString("302e020100300506032b657004220420");
    private const int LongitudFirma = 64;

    private readonly byte[] _privada;

    private FirmanteEd25519(byte[] semilla)
    {
        var par = PublicKeyAuth.GenerateKeyPair(semilla);
        _privada = par.PrivateKey;
        ClavePublica = par.PublicKey;
        ClaveId = IdDe(ClavePublica);
    }

    public string ClaveId { get; }

    public byte[] ClavePublica { get; }

    /// <summary><c>ed25519:</c> más el SHA-256 de la clave pública: la huella que se publica.</summary>
    public static string IdDe(byte[] clavePublica) =>
        "ed25519:" + Convert.ToHexString(SHA256.HashData(clavePublica)).ToLowerInvariant();

    public static FirmanteEd25519 DesdeArchivo(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta))
            throw new InvalidOperationException(
                $"No se encontró la clave de sellado en «{ruta}». Configurá {OpcionesSellado.Seccion}:RutaClavePrivada " +
                "apuntando a un secreto montado (p. ej. /run/secrets/clave-sellado), nunca a un archivo del repo.");
        return DesdePem(File.ReadAllText(ruta));
    }

    public static FirmanteEd25519 DesdePem(string pem)
    {
        var der = LeerPkcs8(pem);
        try
        {
            if (der is null || der.Length != PrefijoPkcs8.Length + 32 || !der.AsSpan(0, PrefijoPkcs8.Length).SequenceEqual(PrefijoPkcs8))
                throw new InvalidOperationException(
                    "La clave de sellado debe ser Ed25519 en PKCS#8 PEM (openssl genpkey -algorithm ed25519).");
            return new FirmanteEd25519(der[PrefijoPkcs8.Length..]);
        }
        finally
        {
            if (der is not null)
                CryptographicOperations.ZeroMemory(der);
        }
    }

    public byte[] Firmar(byte[] mensaje) => PublicKeyAuth.SignDetached(mensaje, _privada);

    public bool Verificar(SelloDeCadena sello) =>
        sello.ClaveId == ClaveId
        && sello.Firma.Length == LongitudFirma
        && PublicKeyAuth.VerifyDetached(sello.Firma, sello.MensajeCanonico(), ClavePublica);

    private static byte[]? LeerPkcs8(string pem)
    {
        if (string.IsNullOrEmpty(pem) || !PemEncoding.TryFind(pem, out var campos) || pem[campos.Label] != "PRIVATE KEY")
            return null;
        var der = new byte[campos.DecodedDataLength];
        return Convert.TryFromBase64Chars(pem.AsSpan()[campos.Base64Data], der, out var escritos) ? der[..escritos] : null;
    }
}
