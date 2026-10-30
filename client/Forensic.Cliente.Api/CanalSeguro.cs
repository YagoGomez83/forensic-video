using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Forensic.Cliente.Api;

/// <summary>
/// El canal con el servidor: TLS mutuo. La workstation presenta su certificado y sólo acepta un servidor
/// cuyo certificado encadene a la CA del laboratorio (no al almacén de confianza del sistema) y cuyo
/// nombre coincida con el de la URL. HTTP en claro, sólo contra loopback (desarrollo).
/// </summary>
public static class CanalSeguro
{
    private static readonly Oid ServidorTls = new("1.3.6.1.5.5.7.3.1");

    public static SocketsHttpHandler CrearManejador(X509Certificate2? certificadoCliente, X509Certificate2 caDelServidor)
    {
        var manejador = new SocketsHttpHandler();
        if (certificadoCliente is not null)
            manejador.SslOptions.ClientCertificates = new X509CertificateCollection { certificadoCliente };
        manejador.SslOptions.RemoteCertificateValidationCallback = (_, certificado, _, errores) =>
            // Los errores de cadena son contra el almacén del sistema: la cadena se valida contra la CA propia.
            (errores & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
            && certificado is X509Certificate2 c && EsDeLaCa(c, caDelServidor);
        return manejador;
    }

    /// <summary>HTTP en claro sólo hacia loopback: cualquier otro servidor exige https.</summary>
    public static void ExigirCanalSeguro(Uri servidor)
    {
        if (servidor.Scheme == Uri.UriSchemeHttps)
            return;
        if (servidor.Scheme == Uri.UriSchemeHttp && servidor.IsLoopback)
            return;
        throw new InvalidOperationException(
            $"El servidor {servidor} no usa https: fuera de esta máquina el canal tiene que ser TLS mutuo.");
    }

    /// <summary>
    /// El manejador según la configuración de la workstation (ya validada por
    /// <see cref="ConfiguracionDeWorkstation"/>): null (el de siempre) para loopback en claro, o el de TLS
    /// mutuo para https, con la CA del laboratorio y el certificado de esta workstation.
    /// </summary>
    public static HttpMessageHandler? Para(ConfiguracionDeWorkstation config)
    {
        ExigirCanalSeguro(config.Servidor);
        if (config.Servidor.Scheme == Uri.UriSchemeHttp)
            return null;

        var ca = X509Certificate2.CreateFromPem(File.ReadAllText(config.CaServidor!));
        return CrearManejador(CargarCertificado(config), ca);
    }

    private static X509Certificate2 CargarCertificado(ConfiguracionDeWorkstation config)
    {
        var valor = config.CertificadoCliente!;
        if (config.CertificadoEnAlmacen)
        {
            using var almacen = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            almacen.Open(OpenFlags.ReadOnly);
            var encontrados = almacen.Certificates.Find(X509FindType.FindByThumbprint, valor, validOnly: false);
            return encontrados.Count == 1 && encontrados[0].HasPrivateKey
                ? encontrados[0]
                : throw new InvalidOperationException($"No está en el almacén personal un certificado con clave y huella {valor}.");
        }
        // En Windows, SslStream no usa una clave efímera cargada de PEM: se re-importa desde PKCS#12.
        using var pem = X509Certificate2.CreateFromPemFile(valor, config.ClaveCliente!);
        return new X509Certificate2(pem.Export(X509ContentType.Pkcs12));
    }

    private static bool EsDeLaCa(X509Certificate2 certificado, X509Certificate2 ca)
    {
        using var cadena = new X509Chain();
        cadena.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        cadena.ChainPolicy.CustomTrustStore.Add(ca);
        cadena.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        cadena.ChainPolicy.ApplicationPolicy.Add(ServidorTls);
        return cadena.Build(certificado);
    }
}
