using Forensic.Application;
using Forensic.Application.Custodia;
using Forensic.Application.Identidad;
using Forensic.Application.Operaciones;
using Forensic.Infrastructure.Almacen;
using Forensic.Application.Informes;
using Forensic.Infrastructure.Identidad;
using Forensic.Infrastructure.Informes;
using Forensic.Infrastructure.Persistencia;
using Forensic.Infrastructure.Sellado;
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

        // Identidad: otro schema y otras migraciones; nunca comparte transacción con la custodia.
        services.AddDbContext<ContextoDeIdentidad>(o => o.UseNpgsql(
            conexion, x => x.MigrationsHistoryTable("__EFMigrationsHistory", ContextoDeIdentidad.Schema)));
        services.AddScoped<RepositorioDeUsuarios>();
        services.AddScoped<IRepositorioDeUsuarios>(sp => sp.GetRequiredService<RepositorioDeUsuarios>());
        services.AddScoped<IAuditoriaDeAccesos>(sp => sp.GetRequiredService<RepositorioDeUsuarios>());
        services.AddScoped<ILectorDeAuditoriaDeAccesos, LectorDeAuditoriaDeAccesos>();
        services.AddSingleton<IHasheadorDeContrasena, HasheadorArgon2id>();
        services.AddOptions<OpcionesJwt>().Bind(configuration.GetSection(OpcionesJwt.Seccion));
        services.AddSingleton<IEmisorDeTokens, EmisorDeTokensJwt>();

        services.AddOptions<OpcionesAlmacen>().Bind(configuration.GetSection(OpcionesAlmacen.Seccion));
        services.AddSingleton<IAlmacenDeEvidencia, AlmacenDeEvidenciaEnDisco>();

        // Sellado de la cadena: la clave del laboratorio es el secreto crítico; sin ella el servidor no arranca.
        services.AddOptions<OpcionesSellado>().Bind(configuration.GetSection(OpcionesSellado.Seccion));
        services.AddSingleton<IFirmanteDeCadena>(sp =>
            FirmanteEd25519.DesdeArchivo(sp.GetRequiredService<IOptions<OpcionesSellado>>().Value.RutaClavePrivada));
        services.AddSingleton<IAlmacenDeSellos, AlmacenDeSellosEnDisco>();
        services.AddHostedService<SelladorPeriodico>();

        services.AddSingleton<IRedactorDeInformes, RedactorDePdf>();

        services.AddOptions<OpcionesWorkerIngesta>().Bind(configuration.GetSection(OpcionesWorkerIngesta.Seccion));
        services.AddHttpClient<IWorkerDeIngesta, WorkerDeIngestaHttp>((sp, http) =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesWorkerIngesta>>().Value;
            http.BaseAddress = opciones.UrlBase
                ?? throw new InvalidOperationException($"Falta configurar {OpcionesWorkerIngesta.Seccion}:UrlBase.");
            http.Timeout = opciones.Timeout;
        });

        services.AddOptions<OpcionesWorkerProcesamiento>().Bind(configuration.GetSection(OpcionesWorkerProcesamiento.Seccion));
        services.AddHttpClient<IWorkerDeProcesamiento, WorkerDeProcesamientoHttp>((sp, http) =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesWorkerProcesamiento>>().Value;
            http.BaseAddress = opciones.UrlBase
                ?? throw new InvalidOperationException($"Falta configurar {OpcionesWorkerProcesamiento.Seccion}:UrlBase.");
            http.Timeout = opciones.Timeout;
        });
        return services;
    }

    /// <summary>Aplica las migraciones de los dos contextos: custodia e identidad.</summary>
    public static async Task MigrarAsync(IServiceProvider servicios)
    {
        await servicios.GetRequiredService<ContextoForense>().Database.MigrateAsync();
        await servicios.GetRequiredService<ContextoDeIdentidad>().Database.MigrateAsync();
    }
}
