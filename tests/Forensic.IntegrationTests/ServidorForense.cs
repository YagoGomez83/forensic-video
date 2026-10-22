using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Forensic.Api.Identidad;
using Forensic.Application;
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

    public string CadenaDeConexion => _postgres.GetConnectionString();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(RutaAlmacen))
        {
            foreach (var f in Directory.EnumerateFiles(RutaAlmacen, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(RutaAlmacen, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Forense", _postgres.GetConnectionString());
        builder.UseSetting("AlmacenDeEvidencia:RutaRaiz", RutaAlmacen);
        builder.UseSetting("Persistencia:MigrarAlIniciar", "true");
        builder.ConfigureTestServices(s => s.AddSingleton<IWorkerDeIngesta>(new WorkerDeIngestaDoble()));
    }

    public HttpClient ClienteDe(string usuario, Rol rol) => CreateClient().ComoActor(usuario, rol);
}

internal static class HttpExtensiones
{
    public static HttpClient ComoActor(this HttpClient cliente, string usuario, Rol rol)
    {
        cliente.DefaultRequestHeaders.Add(ActorDeDesarrollo.CabeceraUsuario, usuario);
        cliente.DefaultRequestHeaders.Add(ActorDeDesarrollo.CabeceraRol, rol.ToString());
        return cliente;
    }

    public static async Task<T> LeerAsync<T>(this HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(ServidorForense.Json))!;
    }
}
