using Forensic.Cliente.Api;

namespace Forensic.Cliente.Tests.Api;

/// <summary>
/// De dónde saca la workstation su configuración: el archivo que escribe el admin en ProgramData o, si no
/// existe (desarrollo), las variables de entorno.
/// </summary>
public sealed class ConfiguracionDeWorkstationTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("forensic-config-").FullName;

    private string Archivo => Path.Combine(_carpeta, "cliente.json");

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private static Func<string, string?> Entorno(params (string Clave, string Valor)[] variables) =>
        nombre => variables.FirstOrDefault(v => v.Clave == nombre).Valor;

    private ConfiguracionDeWorkstation ConArchivo(string json, Func<string, string?>? entorno = null)
    {
        File.WriteAllText(Archivo, json);
        return ConfiguracionDeWorkstation.Cargar(Archivo, entorno ?? Entorno());
    }

    private ConfiguracionInvalidaException Invalida(string json) =>
        Assert.Throws<ConfiguracionInvalidaException>(() => ConArchivo(json));

    [Fact]
    public void Lee_el_archivo_del_admin()
    {
        var config = ConArchivo("""
            {
              "servidor": "https://custodia.lab:8443/",
              "caServidor": "C:\\ProgramData\\ForensicVideo\\ca.crt",
              "certificadoCliente": "0123456789abcdef0123456789abcdef01234567",
              "workerIa": "http://127.0.0.1:8200/"
            }
            """);

        Assert.Equal(new Uri("https://custodia.lab:8443/"), config.Servidor);
        Assert.Equal(@"C:\ProgramData\ForensicVideo\ca.crt", config.CaServidor);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", config.CertificadoCliente);
        Assert.Null(config.ClaveCliente);
        Assert.Equal(new Uri("http://127.0.0.1:8200/"), config.WorkerIa);
        Assert.Equal(Archivo, config.Origen);
    }

    [Fact]
    public void Si_hay_archivo_las_variables_de_entorno_no_cuentan()
    {
        // Una variable de usuario no puede apuntar la workstation a otro servidor ni a otra CA.
        var config = ConArchivo("""{ "servidor": "http://localhost:5065/" }""", Entorno(
            (ConfiguracionDeWorkstation.VariableDelServidor, "https://otro.lab/"),
            (ConfiguracionDeWorkstation.VariableDeLaCa, "otra-ca.crt"),
            (ConfiguracionDeWorkstation.VariableDelWorkerIa, "http://127.0.0.1:9999/")));

        Assert.Equal(new Uri("http://localhost:5065/"), config.Servidor);
        Assert.Null(config.CaServidor);
        Assert.Equal(ClienteWorkerIa.PorDefecto, config.WorkerIa);
    }

    [Fact]
    public void Sin_archivo_usa_las_variables_de_entorno()
    {
        var config = ConfiguracionDeWorkstation.Cargar(Archivo, Entorno(
            (ConfiguracionDeWorkstation.VariableDelServidor, "https://custodia.lab/"),
            (ConfiguracionDeWorkstation.VariableDeLaCa, "ca.crt"),
            (ConfiguracionDeWorkstation.VariableDelCertificado, "cliente.crt"),
            (ConfiguracionDeWorkstation.VariableDeLaClave, "cliente.key")));

        Assert.Equal(new Uri("https://custodia.lab/"), config.Servidor);
        Assert.Equal("ca.crt", config.CaServidor);
        Assert.Equal("cliente.crt", config.CertificadoCliente);
        Assert.Equal("cliente.key", config.ClaveCliente);
        Assert.Equal(ClienteWorkerIa.PorDefecto, config.WorkerIa);
        Assert.Equal("variables de entorno", config.Origen);
    }

    [Fact]
    public void Sin_archivo_ni_variables_es_el_servidor_local_de_desarrollo()
    {
        var config = ConfiguracionDeWorkstation.Cargar(Archivo, Entorno());

        Assert.Equal(new Uri("http://localhost:5065/"), config.Servidor);
    }

    [Fact]
    public void En_el_archivo_el_servidor_es_obligatorio()
    {
        var ex = Invalida("""{ "workerIa": "http://127.0.0.1:8100/" }""");

        Assert.Contains("servidor", ex.Message);
        Assert.Contains(Archivo, ex.Message);
    }

    [Fact]
    public void Una_clave_desconocida_no_pasa_en_silencio()
    {
        // Un error de tipeo («caservidor») dejaría la CA sin configurar sin que nadie lo note.
        var ex = Invalida("""{ "servidor": "https://custodia.lab/", "caservidor": "ca.crt", "certificadoCliente": "c" }""");

        Assert.Contains("caservidor", ex.Message);
    }

    [Theory]
    [InlineData("{ \"servidor\": ")]
    [InlineData("[]")]
    [InlineData("{ \"servidor\": 8443 }")]
    [InlineData("{ \"servidor\": \"custodia.lab\" }")]
    public void Un_archivo_mal_formado_se_rechaza(string json)
    {
        var ex = Invalida(json);

        Assert.Contains(Archivo, ex.Message);
    }

    [Fact]
    public void En_claro_contra_otro_equipo_no_arranca()
    {
        var ex = Invalida("""{ "servidor": "http://custodia.lab:5065/" }""");

        Assert.Contains("https", ex.Message);
    }

    [Fact]
    public void Con_https_exige_la_ca_del_laboratorio()
    {
        var ex = Invalida("""{ "servidor": "https://custodia.lab/", "certificadoCliente": "c.crt" }""");

        Assert.Contains("caServidor", ex.Message);
    }

    [Fact]
    public void Con_https_exige_el_certificado_de_la_workstation()
    {
        var ex = Invalida("""{ "servidor": "https://custodia.lab/", "caServidor": "ca.crt" }""");

        Assert.Contains("certificadoCliente", ex.Message);
    }

    [Fact]
    public void Un_certificado_pem_exige_su_clave()
    {
        var pem = Path.Combine(_carpeta, "workstation-01.crt");
        File.WriteAllText(pem, "");

        var ex = Invalida($$"""{ "servidor": "https://custodia.lab/", "caServidor": "ca.crt", "certificadoCliente": "{{pem.Replace(@"\", @"\\")}}" }""");

        Assert.Contains("claveCliente", ex.Message);
    }

    [Theory]
    [InlineData("huella SHA-1 del certificado de esta workstation (40 hex, la imprime generar-certificados-mtls.sh)")]
    [InlineData("01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01 23 45 67")]
    [InlineData("01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67")]
    public void Lo_que_no_es_huella_ni_archivo_se_rechaza_por_lo_que_es(string valor)
    {
        // El ejemplo sin completar, o una huella copiada con separadores: pedir «claveCliente» confundiría.
        var ex = Invalida($$"""{ "servidor": "https://custodia.lab/", "caServidor": "ca.crt", "certificadoCliente": "{{valor}}" }""");

        Assert.Contains("certificadoCliente", ex.Message);
        Assert.Contains("40", ex.Message);
        Assert.DoesNotContain("claveCliente", ex.Message);
    }

    [Fact]
    public void El_worker_de_ia_tiene_que_ser_local()
    {
        var ex = Invalida("""{ "servidor": "http://localhost:5065/", "workerIa": "http://gpu.lab:8100/" }""");

        Assert.Contains("workerIa", ex.Message);
    }

    [Fact]
    public void Con_variables_el_error_nombra_la_variable()
    {
        var ex = Assert.Throws<ConfiguracionInvalidaException>(() => ConfiguracionDeWorkstation.Cargar(Archivo, Entorno(
            (ConfiguracionDeWorkstation.VariableDelServidor, "https://custodia.lab/"),
            (ConfiguracionDeWorkstation.VariableDelCertificado, "c.crt"))));

        Assert.Contains(ConfiguracionDeWorkstation.VariableDeLaCa, ex.Message);
    }
}
