using Forensic.Application.Casos;
using Forensic.Application.Custodia;
using Forensic.Application.Evidencias;
using Microsoft.Extensions.DependencyInjection;

namespace Forensic.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAplicacion(this IServiceCollection services)
    {
        // Timestamps forenses: siempre del reloj del servidor.
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ServicioDeCasos>();
        services.AddScoped<ServicioDeEvidencias>();
        services.AddScoped<ServicioDeCustodia>();
        return services;
    }
}
