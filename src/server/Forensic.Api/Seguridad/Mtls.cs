using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Forensic.Api.Seguridad;

/// <summary>
/// TLS mutuo entre las workstations y el servidor. Los certificados son rutas a secretos montados
/// (Docker secrets), nunca archivos del repo ni de la imagen. El certificado de cliente identifica el
/// equipo; el usuario lo sigue identificando el JWT.
/// </summary>
public sealed class OpcionesTls
{
    public const string Seccion = "Tls";

    /// <summary>Certificado del servidor en PEM (con su cadena, si la hay).</summary>
    public string? CertificadoServidor { get; set; }

    /// <summary>Clave privada del servidor en PEM (PKCS#8).</summary>
    public string? ClaveServidor { get; set; }

    /// <summary>La CA del laboratorio, en PEM: sólo los certificados de cliente que emitió pueden conectarse.</summary>
    public string? CaDeClientes { get; set; }

    /// <summary>
    /// La CRL de la CA del laboratorio, en PEM o DER. Obligatoria con mTLS: se relee en cada conexión, así
    /// que revocar una workstation es copiar la CRL nueva, sin reiniciar.
    /// </summary>
    public string? CrlDeClientes { get; set; }

    public string Direccion { get; set; } = "0.0.0.0";

    public int Puerto { get; set; } = 8443;

    /// <summary>
    /// Apaga el TLS en Production a propósito, por ejemplo detrás de un proxy que termina el mTLS. Sin
    /// esto, el servidor en Production no arranca sin certificados.
    /// </summary>
    public bool SinTls { get; set; }

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(CertificadoServidor) || !string.IsNullOrWhiteSpace(ClaveServidor) || !string.IsNullOrWhiteSpace(CaDeClientes)
        || !string.IsNullOrWhiteSpace(CrlDeClientes);
}

public static class Mtls
{
    private static readonly Oid ClienteTls = new("1.3.6.1.5.5.7.3.2");

    /// <summary>
    /// Kestrel escuchando sólo en HTTPS, exigiendo certificado de cliente y aceptando únicamente los que
    /// encadenan a la CA del laboratorio, vigentes, con uso de autenticación de cliente y que no estén en
    /// la CRL de esa CA. El almacén de confianza del sistema no cuenta. Sin la CRL, o con una que no es de
    /// la CA o ya vencida, no arranca.
    /// </summary>
    public static void ConfigurarKestrel(KestrelServerOptions kestrel, OpcionesTls opciones, ILogger logger, TimeProvider reloj)
    {
        if (string.IsNullOrWhiteSpace(opciones.CertificadoServidor) || string.IsNullOrWhiteSpace(opciones.ClaveServidor)
            || string.IsNullOrWhiteSpace(opciones.CaDeClientes) || string.IsNullOrWhiteSpace(opciones.CrlDeClientes))
            throw new InvalidOperationException(
                $"mTLS incompleto: definí {OpcionesTls.Seccion}:CertificadoServidor, {OpcionesTls.Seccion}:ClaveServidor, " +
                $"{OpcionesTls.Seccion}:CaDeClientes y {OpcionesTls.Seccion}:CrlDeClientes (rutas a secretos montados; ver " +
                "scripts/generar-certificados-mtls.sh).");

        var servidor = Cargar(() => X509Certificate2.CreateFromPemFile(opciones.CertificadoServidor, opciones.ClaveServidor), "del servidor");
        var ca = Cargar(() => X509Certificate2.CreateFromPem(File.ReadAllText(opciones.CaDeClientes)), "de la CA de clientes");
        // En Windows, SslStream no usa una clave efímera cargada de PEM: se re-importa desde PKCS#12.
        servidor = new X509Certificate2(servidor.Export(X509ContentType.Pkcs12));
        var revocacion = new RevocacionDeClientes(opciones.CrlDeClientes, ca, logger, reloj);

        kestrel.Listen(IPAddress.Parse(opciones.Direccion), opciones.Puerto, escucha => escucha.UseHttps(https =>
        {
            https.ServerCertificate = servidor;
            https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.ClientCertificateValidation = (certificado, _, _) =>
            {
                if (!EsDeLaCa(certificado, ca))
                {
                    logger.LogWarning("Conexión rechazada: certificado de cliente que no emitió la CA del laboratorio ({Sujeto}, {Huella}).",
                        certificado.Subject, certificado.Thumbprint);
                    return false;
                }
                return revocacion.Admite(certificado);
            };
        }));
    }

    private static bool EsDeLaCa(X509Certificate2 certificado, X509Certificate2 ca)
    {
        using var cadena = new X509Chain();
        cadena.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        cadena.ChainPolicy.CustomTrustStore.Add(ca);
        cadena.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        cadena.ChainPolicy.ApplicationPolicy.Add(ClienteTls);
        return cadena.Build(certificado);
    }

    private static X509Certificate2 Cargar(Func<X509Certificate2> cargar, string cual)
    {
        try
        {
            return cargar();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException($"No se pudo cargar el certificado {cual} (sección {OpcionesTls.Seccion}): {ex.Message}", ex);
        }
    }
}
