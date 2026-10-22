using Forensic.Application;
using Forensic.Infrastructure.Almacen;
using Forensic.Infrastructure.Persistencia;
using Forensic.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Forensic.Infrastructure;

public static class DependencyInjection
{
    public const string CadenaDeConexion = "Forense";

    public static IServiceCollection AddInfraestructura(this IServiceCollection services, IConfiguration configuration)
    {
        var conexion = configuration.GetConnectionString(CadenaDeConexion)
            ?? throw new InvalidOperationException(
                $"Falta ConnectionStrings:{CadenaDeConexion} (user-secrets o variable ConnectionStrings__{CadenaDeConexion}).");

        services.AddDbContext<ContextoForense>(o => o.UseNpgsql(conexion));
        services.AddScoped<IPersistenciaForense, PersistenciaForense>();

        services.AddOptions<OpcionesAlmacen>().Bind(configuration.GetSection(OpcionesAlmacen.Seccion));
        services.AddSingleton<IAlmacenDeEvidencia, AlmacenDeEvidenciaEnDisco>();

        services.AddOptions<OpcionesWorkerIngesta>().Bind(configuration.GetSection(OpcionesWorkerIngesta.Seccion));
        services.AddHttpClient<IWorkerDeIngesta, WorkerDeIngestaHttp>((sp, http) =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesWorkerIngesta>>().Value;
            http.BaseAddress = opciones.UrlBase
                ?? throw new InvalidOperationException($"Falta configurar {OpcionesWorkerIngesta.Seccion}:UrlBase.");
            http.Timeout = opciones.Timeout;
        });
        return services;
    }
}
