using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Forensic.Api.Controllers;
using Forensic.Application;
using Forensic.Application.Casos;
using Forensic.Application.Custodia;
using Forensic.Application.Evidencias;
using Forensic.Application.Identidad;
using Forensic.Application.Operaciones;
using Forensic.Domain.Comun;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Forensic.IntegrationTests;

/// <summary>API completa contra un PostgreSQL efímero y un almacén en un directorio temporal.</summary>
public sealed class ServidorForense : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string RutaAlmacen { get; } = Path.Combine(Path.GetTempPath(), "forensic-video-tests", Guid.NewGuid().ToString("N"));

    public string RutaSellos { get; } = Path.Combine(Path.GetTempPath(), "forensic-video-tests", "sellos-" + Guid.NewGuid().ToString("N"));

    /// <summary>Clave de sellado efímera, sólo de este servidor de tests.</summary>
    public string RutaClaveDeSellado { get; } = ClaveDePrueba.EnArchivoTemporal();

    public string CadenaDeConexion => _postgres.GetConnectionString();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        foreach (var dir in new[] { RutaAlmacen, RutaSellos })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
        File.Delete(RutaClaveDeSellado);
    }

    /// <summary>Una vuelta del sellado periódico, que en los tests está apagado.</summary>
    public async Task SellarPendientesAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ServicioDeSellado>().SellarPendientesAsync(CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Forense", _postgres.GetConnectionString());
        builder.UseSetting("AlmacenDeEvidencia:RutaRaiz", RutaAlmacen);
        builder.UseSetting("Persistencia:MigrarAlIniciar", "true");
        builder.UseSetting("Custodia:Sellado:RutaClavePrivada", RutaClaveDeSellado);
        builder.UseSetting("Custodia:Sellado:RaizDeSellos", RutaSellos);
        builder.UseSetting("Custodia:Sellado:Intervalo", "00:00:00");
        builder.UseSetting("Identidad:Jwt:ClaveDeFirma", ClaveDeFirma);
        builder.UseSetting("Identidad:AdminInicial:Usuario", AdminInicial);
        builder.UseSetting("Identidad:AdminInicial:Contrasena", ContrasenaInicialDelAdmin);
        // Los tests hacen muchos logins desde la misma "IP"; el límite se prueba aparte, con uno bajo.
        builder.UseSetting("Identidad:LimiteDeLogin:IntentosPorVentana", "100000");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IWorkerDeIngesta>(new WorkerDeIngestaDoble());
            s.AddSingleton<IWorkerDeProcesamiento>(new WorkerDeProcesamientoDoble());
            AutenticacionPorCabeceras.Registrar(s);
            s.AddTransient<IStartupFilter, IpDePrueba>();
        });
    }

    /// <summary>Sólo de tests. En un entorno real la clave viene de secretos.</summary>
    public const string ClaveDeFirma = "clave-de-firma-solo-para-tests-0123456789";
    public const string AdminInicial = "adela";
    public const string ContrasenaInicialDelAdmin = "contrasena inicial del admin";
    public const string ContrasenaDelAdmin = "la del admin ya cambiada";

    private readonly SemaphoreSlim _sesionDelAdmin = new(1, 1);
    private string? _tokenDelAdmin;

    public HttpClient ClienteDe(string usuario, Rol rol) => CreateClient().ComoActor(usuario, rol);

    public HttpClient ClienteConToken(string token)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    /// <summary>
    /// El admin sembrado, ya con su contraseña cambiada (la inicial sólo sirve para cambiarla). Se
    /// hace una sola vez por servidor.
    /// </summary>
    public async Task<HttpClient> AdminAsync()
    {
        await _sesionDelAdmin.WaitAsync();
        try
        {
            if (_tokenDelAdmin is null)
            {
                var inicial = await CreateClient().LoginAsync(AdminInicial, ContrasenaInicialDelAdmin);
                _tokenDelAdmin = (await ClienteConToken(inicial.Token).CambiarContrasenaAsync(ContrasenaInicialDelAdmin, ContrasenaDelAdmin)).Token;
            }
            return ClienteConToken(_tokenDelAdmin);
        }
        finally
        {
            _sesionDelAdmin.Release();
        }
    }

    /// <summary>Un usuario creado por el admin que ya cambió su contraseña inicial: sesión completa.</summary>
    public async Task<HttpClient> UsuarioConSesionAsync(string usuario, Rol rol, string contrasena = "contrasena definitiva 2026")
    {
        var admin = await AdminAsync();
        const string inicial = "contrasena temporal 2026";
        (await admin.PostAsJsonAsync("/usuarios", new CrearUsuarioRequest(usuario, rol, inicial), Json)).EnsureSuccessStatusCode();
        var token = await CreateClient().LoginAsync(usuario, inicial);
        return ClienteConToken((await ClienteConToken(token.Token).CambiarContrasenaAsync(inicial, contrasena)).Token);
    }
}

internal static class HttpExtensiones
{
    public static HttpClient ComoActor(this HttpClient cliente, string usuario, Rol rol)
    {
        cliente.DefaultRequestHeaders.Add(AutenticacionPorCabeceras.CabeceraUsuario, usuario);
        cliente.DefaultRequestHeaders.Add(AutenticacionPorCabeceras.CabeceraRol, rol.ToString());
        return cliente;
    }

    public static Task<HttpResponseMessage> PostLoginAsync(this HttpClient cliente, string usuario, string contrasena) =>
        cliente.PostAsJsonAsync("/auth/login", new LoginRequest(usuario, contrasena));

    public static async Task<TokenEmitido> LoginAsync(this HttpClient cliente, string usuario, string contrasena) =>
        await (await cliente.PostLoginAsync(usuario, contrasena)).LeerAsync<TokenEmitido>();

    public static async Task<TokenEmitido> CambiarContrasenaAsync(this HttpClient cliente, string actual, string nueva) =>
        await (await cliente.PostAsJsonAsync("/auth/contrasena", new CambioDeContrasenaRequest(actual, nueva))).LeerAsync<TokenEmitido>();

    public static async Task<CasoDto> AbrirCasoAsync(this HttpClient cliente) =>
        await (await cliente.PostAsJsonAsync("/casos", new AbrirCasoRequest($"EXP-{Guid.NewGuid():N}"[..20], "Prueba")))
            .LeerAsync<CasoDto>();

    public static async Task<HttpResponseMessage> IngestarAsync(
        this HttpClient cliente, Guid casoId, byte[] bytes, string nombre = "cámara 1.mp4")
    {
        var contenido = new ByteArrayContent(bytes);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await cliente.PostAsync($"/casos/{casoId}/evidencias?nombre={Uri.EscapeDataString(nombre)}", contenido);
    }

    public static Task<HttpResponseMessage> AplicarOperacionAsync(
        this HttpClient cliente, Guid casoId, Guid evidenciaId, string tipo, object parametros, string? nombre = null) =>
        cliente.PostAsJsonAsync(
            $"/casos/{casoId}/evidencias/{evidenciaId}/operaciones",
            new AplicarOperacionRequest(tipo, JsonSerializer.SerializeToElement(parametros), nombre));

    public static Task<HttpResponseMessage> ReproducirAsync(this HttpClient cliente, Guid casoId, Guid evidenciaId) =>
        cliente.PostAsync($"/casos/{casoId}/evidencias/{evidenciaId}/reproducir", null);

    public static async Task<List<EvidenciaDto>> EvidenciasAsync(this HttpClient cliente, Guid casoId) =>
        await (await cliente.GetAsync($"/casos/{casoId}/evidencias")).LeerAsync<List<EvidenciaDto>>();

    public static async Task<ExportDeCustodia> ExportarAsync(this HttpClient cliente, Guid casoId) =>
        await (await cliente.GetAsync($"/casos/{casoId}/custodia")).LeerAsync<ExportDeCustodia>();

    public static async Task<T> LeerAsync<T>(this HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(ServidorForense.Json))!;
    }
}
