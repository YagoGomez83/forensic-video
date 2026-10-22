using Forensic.Api.Identidad;
using Forensic.Application;
using Forensic.Domain.Comun;
using Microsoft.AspNetCore.Diagnostics;

namespace Forensic.Api;

/// <summary>Traduce las excepciones de dominio y aplicación a ProblemDetails.</summary>
internal sealed class ManejadorDeErrores(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        (int status, string titulo)? mapeo = exception switch
        {
            NoAutenticadoException => (StatusCodes.Status401Unauthorized, "No autenticado"),
            AccesoDenegadoException => (StatusCodes.Status403Forbidden, "Acceso denegado"),
            NoEncontradoException => (StatusCodes.Status404NotFound, "No encontrado"),
            ConflictoDeCustodiaException => (StatusCodes.Status409Conflict, "Conflicto en la cadena de custodia"),
            // Discrepancia de hash = bloqueo de la evidencia.
            IntegridadComprometidaException => (StatusCodes.Status423Locked, "Integridad de la evidencia comprometida"),
            IngestaRechazadaException => (StatusCodes.Status422UnprocessableEntity, "Ingesta rechazada"),
            WorkerDeIngestaNoDisponibleException => (StatusCodes.Status503ServiceUnavailable, "Worker de ingesta no disponible"),
            ExcepcionDeDominio => (StatusCodes.Status422UnprocessableEntity, "Regla forense violada"),
            _ => null,
        };
        if (mapeo is not { } m)
            return false;

        http.Response.StatusCode = m.status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Status = m.status, Title = m.titulo, Detail = exception.Message },
        });
    }
}
