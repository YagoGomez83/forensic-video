using ForensicVideo.Application;
using ForensicVideo.Infrastructure.Almacen;
using ForensicVideo.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForensicVideo.Infrastructure;

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
        return services;
    }
}
