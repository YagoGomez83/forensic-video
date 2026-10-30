using Forensic.Cliente.Api;

namespace Forensic.Cliente.Tests.Api;

/// <summary>
/// Cómo arma la workstation su canal con el servidor. Qué configuración es válida lo prueba
/// <see cref="ConfiguracionDeWorkstationTests"/>; el TLS mutuo de punta a punta, MtlsTests en el servidor.
/// </summary>
public class CanalSeguroTests
{
    private static ConfiguracionDeWorkstation Config(string servidor, string? ca = null, string? certificado = null) =>
        new(new Uri(servidor), ca, certificado, null, ClienteWorkerIa.PorDefecto, "prueba");

    [Fact]
    public void En_claro_contra_loopback_usa_el_manejador_de_siempre()
    {
        Assert.Null(CanalSeguro.Para(Config("http://localhost:5065/")));
        Assert.Null(CanalSeguro.Para(Config("http://127.0.0.1:5065/")));
    }

    [Fact]
    public void En_claro_contra_otro_equipo_no_arranca()
    {
        // Aunque alguien arme la configuración a mano, sin pasar por Cargar.
        var ex = Assert.Throws<InvalidOperationException>(() => CanalSeguro.Para(Config("http://servidor.lab:5065/")));
        Assert.Contains("https", ex.Message);
    }

    [Fact]
    public void Con_https_un_certificado_que_no_esta_en_el_almacen_no_arranca()
    {
        var ca = Path.Combine(AppContext.BaseDirectory, "ca-de-prueba.crt");
        File.WriteAllText(ca, CaDePrueba());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CanalSeguro.Para(Config("https://servidor.lab/", ca, "0000000000000000000000000000000000000000")));
        Assert.Contains("0000000000000000000000000000000000000000", ex.Message);
    }

    private static string CaDePrueba()
    {
        using var clave = System.Security.Cryptography.ECDsa.Create();
        var pedido = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=CA de prueba", clave, System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var ca = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return ca.ExportCertificatePem();
    }
}
