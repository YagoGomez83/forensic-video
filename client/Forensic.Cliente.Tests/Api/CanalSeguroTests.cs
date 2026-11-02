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

    [Fact]
    public void Una_ca_que_no_se_puede_leer_se_reporta_con_su_ruta()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "no-existe", "ca.crt");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CanalSeguro.Para(Config("https://servidor.lab/", ruta, "0000000000000000000000000000000000000000")));
        Assert.Contains("CA del laboratorio", ex.Message);
        Assert.Contains(ruta, ex.Message);
    }

    // El certificado de la workstation se busca por huella primero en el almacén del usuario y después en
    // el de la máquina: el de la máquina lo comparten todos los usuarios de Windows de la PC (la persona se
    // identifica con su login en el servidor). Qué encontró cada almacén se prueba sin abrir X509Store.

    [Fact]
    public void El_certificado_del_almacen_de_la_maquina_sirve_si_no_esta_en_el_del_usuario()
    {
        using var certificado = CertificadoConClave();

        var elegido = CanalSeguro.ElegirCertificado(certificado.Thumbprint,
            [(CanalSeguro.AlmacenDelUsuario, []), (CanalSeguro.AlmacenDeLaMaquina, [certificado])], out var almacen);

        Assert.Same(certificado, elegido);
        Assert.Equal(CanalSeguro.AlmacenDeLaMaquina, almacen);
    }

    [Fact]
    public void Si_esta_en_los_dos_almacenes_gana_el_del_usuario()
    {
        using var delUsuario = CertificadoConClave();
        using var deLaMaquina = CertificadoConClave();

        var elegido = CanalSeguro.ElegirCertificado(delUsuario.Thumbprint,
            [(CanalSeguro.AlmacenDelUsuario, [delUsuario]), (CanalSeguro.AlmacenDeLaMaquina, [deLaMaquina])], out var almacen);

        Assert.Same(delUsuario, elegido);
        Assert.Equal(CanalSeguro.AlmacenDelUsuario, almacen);
    }

    [Fact]
    public void Si_no_esta_en_ningun_almacen_el_mensaje_nombra_los_dos_y_la_huella()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CanalSeguro.ElegirCertificado("0123",
            [(CanalSeguro.AlmacenDelUsuario, []), (CanalSeguro.AlmacenDeLaMaquina, [])], out _));

        Assert.Contains("0123", ex.Message);
        Assert.Contains(CanalSeguro.AlmacenDelUsuario, ex.Message);
        Assert.Contains(CanalSeguro.AlmacenDeLaMaquina, ex.Message);
    }

    [Fact]
    public void Un_certificado_sin_clave_privada_se_reporta_como_tal_y_no_como_ausente()
    {
        // Lo que queda si se importa el .crt en lugar del .pfx.
        using var conClave = CertificadoConClave();
        using var sinClave = new System.Security.Cryptography.X509Certificates.X509Certificate2(conClave.RawData);

        var ex = Assert.Throws<InvalidOperationException>(() => CanalSeguro.ElegirCertificado(sinClave.Thumbprint,
            [(CanalSeguro.AlmacenDelUsuario, []), (CanalSeguro.AlmacenDeLaMaquina, [sinClave])], out _));

        Assert.Contains("clave privada", ex.Message);
        Assert.Contains(".pfx", ex.Message);
        Assert.Contains(CanalSeguro.AlmacenDeLaMaquina, ex.Message);
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 CertificadoConClave()
    {
        using var clave = System.Security.Cryptography.ECDsa.Create();
        var pedido = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=workstation-de-prueba", clave, System.Security.Cryptography.HashAlgorithmName.SHA256);
        return pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
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
