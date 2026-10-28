using System.Security.Cryptography;

namespace Forensic.IntegrationTests;

/// <summary>
/// Claves Ed25519 de tests, en PKCS#8 PEM como las genera <c>openssl genpkey -algorithm ed25519</c>.
/// Sólo existen en memoria o en un temporal del test; la clave real se genera fuera del repo.
/// </summary>
internal static class ClaveDePrueba
{
    private static readonly byte[] PrefijoPkcs8 = Convert.FromHexString("302e020100300506032b657004220420");

    public static string Pem(byte[] semilla) =>
        $"-----BEGIN PRIVATE KEY-----\n{Convert.ToBase64String([.. PrefijoPkcs8, .. semilla])}\n-----END PRIVATE KEY-----\n";

    public static string Nueva() => Pem(RandomNumberGenerator.GetBytes(32));

    /// <summary>Escribe una clave nueva en un temporal y devuelve su ruta.</summary>
    public static string EnArchivoTemporal()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "forensic-video-tests", $"clave-{Guid.NewGuid():N}.pem");
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, Nueva());
        return ruta;
    }
}
