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
    /// mutuo para https, con la CA del laboratorio y el certificado de esta workstation. <paramref name="informar"/>
    /// recibe de dónde salió el certificado (lo imprime <c>--comprobar</c>).
    /// </summary>
    public static HttpMessageHandler? Para(ConfiguracionDeWorkstation config, Action<string>? informar = null)
    {
        ExigirCanalSeguro(config.Servidor);
        if (config.Servidor.Scheme == Uri.UriSchemeHttp)
            return null;

        var ca = Cargar("la CA del laboratorio", config.CaServidor!, () => X509Certificate2.CreateFromPem(File.ReadAllText(config.CaServidor!)));
        return CrearManejador(CargarCertificado(config, informar), ca);
    }

    /// <summary>Un archivo de la configuración que no se puede leer: el mensaje dice cuál, para quien instala.</summary>
    private static X509Certificate2 Cargar(string que, string ruta, Func<X509Certificate2> cargar)
    {
        try
        {
            return cargar();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException($"No se pudo leer {que} en {ruta}: {ex.Message}", ex);
        }
    }

    private static X509Certificate2 CargarCertificado(ConfiguracionDeWorkstation config, Action<string>? informar)
    {
        var valor = config.CertificadoCliente!;
        if (config.CertificadoEnAlmacen)
        {
            // El de la máquina lo comparten todos los usuarios de Windows de la PC: la persona se identifica
            // con su login en el servidor, no con su cuenta de Windows.
            var certificado = ElegirCertificado(valor,
                [(AlmacenDelUsuario, Buscar(StoreLocation.CurrentUser, valor)), (AlmacenDeLaMaquina, Buscar(StoreLocation.LocalMachine, valor))],
                out var almacen);
            informar?.Invoke($"Certificado:   {valor} ({almacen})");
            return certificado;
        }
        // En Windows, SslStream no usa una clave efímera cargada de PEM: se re-importa desde PKCS#12.
        using var pem = Cargar("el certificado de esta workstation", valor, () => X509Certificate2.CreateFromPemFile(valor, config.ClaveCliente!));
        informar?.Invoke($"Certificado:   {valor} (archivo PEM)");
        return new X509Certificate2(pem.Export(X509ContentType.Pkcs12));
    }

    private static X509Certificate2Collection Buscar(StoreLocation ubicacion, string huella)
    {
        // Fuera de Windows (los tests en Linux) .NET no abre LocalMachine\My: no hay almacén de la máquina.
        if (ubicacion == StoreLocation.LocalMachine && !OperatingSystem.IsWindows())
            return [];
        using var almacen = new X509Store(StoreName.My, ubicacion);
        almacen.Open(OpenFlags.ReadOnly);
        return almacen.Certificates.Find(X509FindType.FindByThumbprint, huella, validOnly: false);
    }

    internal const string AlmacenDelUsuario = "almacén personal del usuario";
    internal const string AlmacenDeLaMaquina = "almacén de la máquina";

    /// <summary>
    /// El certificado de la workstation entre lo que encontró cada almacén por su huella, en orden: el primero
    /// que lo tiene manda. Sin clave privada, o sin permiso para usarla, se dice eso y no «no está».
    /// </summary>
    internal static X509Certificate2 ElegirCertificado(
        string huella, IReadOnlyList<(string Nombre, X509Certificate2Collection Encontrados)> almacenes, out string almacen)
    {
        foreach (var (nombre, encontrados) in almacenes)
        {
            if (encontrados.Count == 0)
                continue;
            var certificado = encontrados[0];
            if (!certificado.HasPrivateKey)
                throw new InvalidOperationException(
                    $"El certificado con huella {huella} del {nombre} no tiene clave privada: hay que importar el .pfx, no el .crt.");
            ExigirClaveUsable(certificado, huella, nombre);
            almacen = nombre;
            return certificado;
        }
        throw new InvalidOperationException(
            $"No hay un certificado con huella {huella} ni en el {AlmacenDelUsuario} ni en el {AlmacenDeLaMaquina}.");
    }

    /// <summary>
    /// En el almacén de la máquina, la clave privada es legible sólo para quien tenga permiso sobre ella: si este
    /// usuario de Windows no lo tiene, sin esto el error aparecería recién en el handshake y sin decir por qué.
    /// </summary>
    private static void ExigirClaveUsable(X509Certificate2 certificado, string huella, string almacen)
    {
        try
        {
            using AsymmetricAlgorithm? clave = (AsymmetricAlgorithm?)certificado.GetECDsaPrivateKey() ?? certificado.GetRSAPrivateKey();
            if (clave is not null)
                return;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"Este usuario de Windows no puede usar la clave privada del certificado {huella} del {almacen} ({ex.Message}): " +
                "un administrador tiene que correr scripts/instalar-certificado-workstation.ps1 en esta PC.", ex);
        }
        throw new InvalidOperationException($"El certificado {huella} del {almacen} no tiene una clave ECDSA ni RSA.");
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
