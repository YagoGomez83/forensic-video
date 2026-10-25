using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Forensic.Api.Identidad;

public sealed class OpcionesDeLimiteDeLogin
{
    public const string Seccion = "Identidad:LimiteDeLogin";

    public int IntentosPorVentana { get; set; } = 10;
    public TimeSpan Ventana { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Límite de intentos por IP en los endpoints que verifican una contraseña. Es la defensa primaria
/// contra la adivinación paralelizada y el password spraying: el lockout es por cuenta y no frena
/// muchos usuarios a la vez ni ráfagas concurrentes. Lo que se rechaza acá no llega a calcular
/// Argon2id, así que también acota el costo de una ráfaga.
/// </summary>
internal static class LimiteDeLogin
{
    public const string Politica = "LoginPorIp";

    public static void Registrar(IServiceCollection servicios, IConfiguration configuracion)
    {
        var opciones = configuracion.GetSection(OpcionesDeLimiteDeLogin.Seccion).Get<OpcionesDeLimiteDeLogin>()
            ?? new OpcionesDeLimiteDeLogin();
        servicios.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Politica, http => RateLimitPartition.GetFixedWindowLimiter(
                // Sin IP conocida, todas comparten la misma partición: más restrictivo, nunca menos.
                http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = opciones.IntentosPorVentana,
                    Window = opciones.Ventana,
                    QueueLimit = 0,
                }));
            o.OnRejected = (contexto, _) =>
            {
                contexto.HttpContext.Response.Headers.RetryAfter =
                    ((int)opciones.Ventana.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
    }
}
