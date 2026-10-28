using Forensic.Application.Custodia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forensic.Infrastructure.Sellado;

/// <summary>
/// Sella las cadenas al arrancar y después cada <see cref="OpcionesSellado.Intervalo"/>. Una vuelta
/// que falla se registra y no detiene las siguientes.
/// </summary>
internal sealed class SelladorPeriodico(
    IServiceScopeFactory scopes, IOptions<OpcionesSellado> opciones, TimeProvider reloj, ILogger<SelladorPeriodico> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = opciones.Value.Intervalo;
        if (intervalo <= TimeSpan.Zero)
        {
            logger.LogWarning("El sellado periódico de la cadena está apagado ({Seccion}:Intervalo = {Intervalo}).",
                OpcionesSellado.Seccion, intervalo);
            return;
        }

        await Task.Yield();
        using var timer = new PeriodicTimer(intervalo, reloj);
        do
        {
            await SellarAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SellarAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var agregados = await scope.ServiceProvider.GetRequiredService<ServicioDeSellado>().SellarPendientesAsync(ct);
            if (agregados > 0)
                logger.LogInformation("Sellado de la cadena: {Agregados} sellos nuevos.", agregados);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falló una vuelta del sellado periódico de la cadena.");
        }
    }
}
