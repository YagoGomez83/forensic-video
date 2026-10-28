using Forensic.Cliente.Api;

namespace Forensic.Cliente.Tests.Api;

/// <summary>Cómo arma la workstation su canal con el servidor, según sus variables de entorno.</summary>
public class CanalSeguroTests
{
    private static Func<string, string?> Entorno(params (string Clave, string Valor)[] variables) =>
        nombre => variables.FirstOrDefault(v => v.Clave == nombre).Valor;

    [Fact]
    public void En_claro_contra_loopback_usa_el_manejador_de_siempre()
    {
        Assert.Null(CanalSeguro.DesdeEntorno(new Uri("http://localhost:5065/"), Entorno()));
        Assert.Null(CanalSeguro.DesdeEntorno(new Uri("http://127.0.0.1:5065/"), Entorno()));
    }

    [Fact]
    public void En_claro_contra_otro_equipo_no_arranca()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CanalSeguro.DesdeEntorno(new Uri("http://servidor.lab:5065/"), Entorno()));
        Assert.Contains("https", ex.Message);
    }

    [Fact]
    public void Con_https_exige_la_ca_del_laboratorio()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CanalSeguro.DesdeEntorno(new Uri("https://servidor.lab/"), Entorno((CanalSeguro.VariableDelCertificado, "c.crt"))));
        Assert.Contains(CanalSeguro.VariableDeLaCa, ex.Message);
    }

    [Fact]
    public void Con_https_exige_el_certificado_de_la_workstation()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CanalSeguro.DesdeEntorno(new Uri("https://servidor.lab/"), Entorno((CanalSeguro.VariableDeLaCa, "ca.crt"))));
        Assert.Contains(CanalSeguro.VariableDelCertificado, ex.Message);
    }
}
