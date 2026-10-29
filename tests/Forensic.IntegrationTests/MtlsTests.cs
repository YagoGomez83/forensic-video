using System.Net;
using System.Security.Cryptography.X509Certificates;
using Forensic.Api.Seguridad;
using Forensic.Cliente.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Forensic.IntegrationTests;

/// <summary>
/// mTLS cliente↔servidor con Kestrel real (TestServer no hace TLS): el servidor sólo acepta workstations
/// con un certificado de la CA del laboratorio y el cliente sólo habla con un servidor de esa CA. El
/// certificado identifica el equipo; el usuario lo sigue identificando el JWT.
/// </summary>
public sealed class MtlsTests : IAsyncLifetime
{
    private readonly CertificadosDePrueba _laboratorio = new();
    private readonly string _secretos = Path.Combine(Path.GetTempPath(), "forensic-video-tests", $"tls-{Guid.NewGuid():N}");
    private readonly RelojDePrueba _reloj = new(DateTimeOffset.UtcNow);
    private WebApplication? _host;
    private Uri _url = null!;

    public async Task InitializeAsync() => _url = await LevantarAsync(_laboratorio.Servidor());

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        if (Directory.Exists(_secretos))
            Directory.Delete(_secretos, recursive: true);
    }

    /// <summary>Un host mínimo con la misma configuración de Kestrel que el servidor real, desde archivos PEM.</summary>
    private async Task<Uri> LevantarAsync(X509Certificate2 certificadoDelServidor, byte[]? crl = null)
    {
        var opciones = Opciones(certificadoDelServidor, crl ?? _laboratorio.Crl());

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => Mtls.ConfigurarKestrel(k, opciones, NullLogger.Instance, _reloj));
        _host = builder.Build();
        _host.MapGet("/ping", () => "pong");
        await _host.StartAsync();
        var direccion = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Uri(direccion.Replace("127.0.0.1", "localhost") + "/");
    }

    private OpcionesTls Opciones(X509Certificate2 certificadoDelServidor, byte[] crl)
    {
        var (cert, clave) = CertificadosDePrueba.EnPem(certificadoDelServidor, _secretos, "servidor");
        var (ca, _) = CertificadosDePrueba.EnPem(_laboratorio.CaPublica, _secretos, "ca");
        EscribirCrl(crl);
        return new OpcionesTls
        {
            CertificadoServidor = cert, ClaveServidor = clave, CaDeClientes = ca, CrlDeClientes = RutaDeLaCrl, Direccion = "127.0.0.1", Puerto = 0,
        };
    }

    private string RutaDeLaCrl => Path.Combine(_secretos, "ca.crl");

    /// <summary>Como el operador que copia la CRL nueva al servidor: en PEM, sobre el mismo archivo.</summary>
    private void EscribirCrl(byte[] crl)
    {
        Directory.CreateDirectory(_secretos);
        File.WriteAllText(RutaDeLaCrl, CertificadosDePrueba.CrlEnPem(crl));
    }

    private HttpClient Cliente(X509Certificate2? certificado, X509Certificate2? caDelServidor = null) =>
        new(CanalSeguro.CrearManejador(certificado, caDelServidor ?? _laboratorio.CaPublica)) { BaseAddress = _url };

    [Fact]
    public async Task Una_workstation_con_certificado_del_laboratorio_entra()
    {
        using var http = Cliente(_laboratorio.Cliente());

        Assert.Equal("pong", await http.GetStringAsync("ping"));
    }

    [Fact]
    public async Task Sin_certificado_de_cliente_no_hay_conexion()
    {
        using var http = Cliente(null);

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public async Task Un_certificado_de_otra_ca_no_entra()
    {
        using var http = Cliente(new CertificadosDePrueba("Otra CA").Cliente());

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public async Task Un_certificado_de_la_ca_que_no_es_de_cliente_no_entra()
    {
        using var http = Cliente(_laboratorio.Servidor("workstation-01"));

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public async Task Un_certificado_vencido_no_entra()
    {
        using var http = Cliente(_laboratorio.Cliente(vence: DateTimeOffset.UtcNow.AddDays(-1)));

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public async Task El_cliente_rechaza_un_servidor_de_otra_ca()
    {
        // Un intermediario con un certificado propio: el cliente sólo confía en la CA del laboratorio.
        using var http = Cliente(_laboratorio.Cliente(), caDelServidor: new CertificadosDePrueba("CA del intermediario").CaPublica);

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public async Task El_cliente_rechaza_un_servidor_con_otro_nombre()
    {
        await _host!.DisposeAsync();
        _url = await LevantarAsync(_laboratorio.Servidor("otro-servidor.lab"));
        using var http = Cliente(_laboratorio.Cliente());

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public void El_cliente_sin_tls_solo_habla_con_loopback()
    {
        Assert.Throws<InvalidOperationException>(() => CanalSeguro.ExigirCanalSeguro(new Uri("http://servidor.lab:5065/")));
        CanalSeguro.ExigirCanalSeguro(new Uri("http://localhost:5065/"));
        CanalSeguro.ExigirCanalSeguro(new Uri("https://servidor.lab/"));
    }

    [Fact]
    public void Con_rutas_de_tls_incompletas_el_servidor_no_arranca()
    {
        var opciones = new OpcionesTls { CertificadoServidor = Path.Combine(_secretos, "servidor.crt") };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Mtls.ConfigurarKestrel(new Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions(), opciones, NullLogger.Instance, _reloj));
        Assert.Contains("Tls:", ex.Message);
    }

    [Fact]
    public async Task Un_certificado_revocado_no_entra_y_los_demas_si()
    {
        var robada = _laboratorio.Cliente("workstation-robada");
        await _host!.DisposeAsync();
        _url = await LevantarAsync(_laboratorio.Servidor(), _laboratorio.Crl([robada]));

        using var conLaRobada = Cliente(robada);
        await Assert.ThrowsAsync<HttpRequestException>(() => conLaRobada.GetStringAsync("ping"));
        using var conOtra = Cliente(_laboratorio.Cliente("workstation-02"));
        Assert.Equal("pong", await conOtra.GetStringAsync("ping"));
    }

    [Fact]
    public async Task Una_crl_nueva_se_aplica_sin_reiniciar_el_servidor()
    {
        var robada = _laboratorio.Cliente("workstation-robada");
        using (var antes = Cliente(robada))
            Assert.Equal("pong", await antes.GetStringAsync("ping"));

        EscribirCrl(_laboratorio.Crl([robada]));

        using var despues = Cliente(robada);
        await Assert.ThrowsAsync<HttpRequestException>(() => despues.GetStringAsync("ping"));
    }

    [Fact]
    public async Task Con_la_crl_vencida_no_entra_nadie()
    {
        _reloj.Ahora = DateTimeOffset.UtcNow.AddDays(366);

        using var http = Cliente(_laboratorio.Cliente());
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public async Task Si_la_crl_se_reemplaza_por_una_que_no_es_de_la_ca_no_entra_nadie()
    {
        File.WriteAllText(RutaDeLaCrl, CertificadosDePrueba.CrlEnPem(new CertificadosDePrueba().Crl()));

        using var http = Cliente(_laboratorio.Cliente());
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("ping"));
    }

    [Fact]
    public void Sin_crl_de_clientes_el_servidor_no_arranca()
    {
        var opciones = Opciones(_laboratorio.Servidor(), _laboratorio.Crl());
        opciones.CrlDeClientes = null;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Mtls.ConfigurarKestrel(new Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions(), opciones, NullLogger.Instance, _reloj));
        Assert.Contains("Tls:CrlDeClientes", ex.Message);
    }

    [Fact]
    public void Con_una_crl_de_otra_ca_el_servidor_no_arranca()
    {
        var opciones = Opciones(_laboratorio.Servidor(), new CertificadosDePrueba("Otra CA").Crl());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Mtls.ConfigurarKestrel(new Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions(), opciones, NullLogger.Instance, _reloj));
        Assert.Contains("CRL", ex.Message);
    }

    [Fact]
    public void Con_la_crl_vencida_el_servidor_no_arranca()
    {
        var opciones = Opciones(_laboratorio.Servidor(), _laboratorio.Crl(proxima: DateTimeOffset.UtcNow.AddDays(-1)));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Mtls.ConfigurarKestrel(new Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions(), opciones, NullLogger.Instance, _reloj));
        Assert.Contains("venci", ex.Message);
    }
}

internal sealed class RelojDePrueba(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;

    public override DateTimeOffset GetUtcNow() => Ahora;
}

/// <summary>El servidor real: en Production no arranca sin TLS salvo que se lo apague explícitamente.</summary>
public sealed class TlsEnProduccionTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    [Fact]
    public void En_produccion_sin_certificados_el_servidor_no_arranca()
    {
        using var enProduccion = servidor.WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var ex = Assert.ThrowsAny<Exception>(() => enProduccion.CreateClient());
        Assert.Contains("mTLS", ex.ToString());
    }

    [Fact]
    public async Task En_produccion_con_tls_apagado_explicitamente_arranca()
    {
        using var detrasDeUnProxy = servidor.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Tls:SinTls", "true");
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await detrasDeUnProxy.CreateClient().GetAsync("/casos")).StatusCode);
    }
}
