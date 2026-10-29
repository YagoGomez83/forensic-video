using System.Security.Cryptography.X509Certificates;
using System.Text;
using Forensic.Api.Seguridad;

namespace Forensic.IntegrationTests;

/// <summary>
/// La CRL de la CA del laboratorio, leída por el servidor sin red: sólo cuenta si la firmó esa CA, y
/// tiene vigencia propia.
/// </summary>
public sealed class CrlDeClientesTests
{
    private readonly CertificadosDePrueba _laboratorio = new();

    [Fact]
    public void Una_crl_vacia_de_la_ca_no_revoca_a_nadie()
    {
        var crl = CrlDeClientes.Cargar(_laboratorio.Crl(), _laboratorio.CaPublica);

        Assert.Equal(0, crl.CantidadDeRevocados);
        Assert.False(crl.EstaRevocado(_laboratorio.Cliente()));
    }

    [Fact]
    public void Revoca_los_certificados_que_lista_y_ningun_otro()
    {
        var robada = _laboratorio.Cliente("workstation-robada");
        var otra = _laboratorio.Cliente("workstation-02");

        var crl = CrlDeClientes.Cargar(_laboratorio.Crl([robada]), _laboratorio.CaPublica);

        Assert.Equal(1, crl.CantidadDeRevocados);
        Assert.True(crl.EstaRevocado(robada));
        Assert.False(crl.EstaRevocado(otra));
    }

    [Fact]
    public void Acepta_la_crl_en_pem()
    {
        var robada = _laboratorio.Cliente();
        var pem = Encoding.ASCII.GetBytes(CertificadosDePrueba.CrlEnPem(_laboratorio.Crl([robada])));

        Assert.True(CrlDeClientes.Cargar(pem, _laboratorio.CaPublica).EstaRevocado(robada));
    }

    [Fact]
    public void Una_crl_de_otra_ca_con_el_mismo_nombre_se_rechaza_por_la_firma()
    {
        var impostora = new CertificadosDePrueba(); // mismo nombre de CA, otra clave

        var ex = Assert.Throws<InvalidOperationException>(() => CrlDeClientes.Cargar(impostora.Crl(), _laboratorio.CaPublica));
        Assert.Contains("firma", ex.Message);
    }

    [Fact]
    public void Una_crl_de_otra_ca_se_rechaza_por_el_emisor()
    {
        var otra = new CertificadosDePrueba("Otra CA");

        var ex = Assert.Throws<InvalidOperationException>(() => CrlDeClientes.Cargar(otra.Crl(), _laboratorio.CaPublica));
        Assert.Contains("emisor", ex.Message);
    }

    [Fact]
    public void Una_crl_alterada_se_rechaza()
    {
        // Quitar a alguien de la lista cambiando su número de serie: la firma ya no cierra.
        var robada = _laboratorio.Cliente();
        var der = _laboratorio.Crl([robada]);
        var serie = robada.SerialNumberBytes.ToArray();
        var posicion = der.AsSpan().IndexOf(serie);
        Assert.True(posicion > 0);
        der[posicion + serie.Length - 1] ^= 0x01;

        Assert.Throws<InvalidOperationException>(() => CrlDeClientes.Cargar(der, _laboratorio.CaPublica));
    }

    [Fact]
    public void Lo_que_no_es_una_crl_se_rechaza()
    {
        Assert.Throws<InvalidOperationException>(() => CrlDeClientes.Cargar("no soy una CRL"u8.ToArray(), _laboratorio.CaPublica));
        Assert.Throws<InvalidOperationException>(() =>
            CrlDeClientes.Cargar(_laboratorio.CaPublica.RawData, _laboratorio.CaPublica));
    }

    /// <summary>
    /// Vector cruzado: la CRL que emiten los scripts del repo con openssl (generar-certificados-mtls.sh y
    /// revocar-certificado-mtls.sh workstation-02), no la del builder de .NET. Sólo archivos públicos.
    /// No se mira la vigencia: la CRL del vector vence en 2027 y el test no tiene que vencer con ella.
    /// </summary>
    [Fact]
    public void Lee_la_crl_que_emiten_los_scripts_con_openssl()
    {
        static X509Certificate2 Certificado(string nombre) =>
            X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "crl-openssl", nombre)));
        var der = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Datos", "crl-openssl", "ca.crl"));

        var crl = CrlDeClientes.Cargar(der, Certificado("ca.crt"));

        Assert.Equal(1, crl.CantidadDeRevocados);
        Assert.True(crl.EstaRevocado(Certificado("workstation-02.crt")));
        Assert.False(crl.EstaRevocado(Certificado("workstation-01.crt")));
        Assert.Throws<InvalidOperationException>(() => CrlDeClientes.Cargar(der, _laboratorio.CaPublica));
    }

    [Fact]
    public void Tiene_vigencia_y_avisa_treinta_dias_antes_de_vencer()
    {
        var emitida = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var proxima = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var crl = CrlDeClientes.Cargar(_laboratorio.Crl(proxima: proxima, emitida: emitida), _laboratorio.CaPublica);

        Assert.Equal(emitida, crl.EmitidaEn);
        Assert.Equal(proxima, crl.ProximaActualizacion);
        Assert.False(crl.PorVencer(proxima.AddDays(-31)));
        Assert.True(crl.PorVencer(proxima.AddDays(-29)));
        Assert.False(crl.Vencida(proxima.AddSeconds(-1)));
        Assert.True(crl.Vencida(proxima));
    }
}
