using ForensicVideo.Application.Casos;
using ForensicVideo.Application.Custodia;
using ForensicVideo.Application.Evidencias;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace ForensicVideo.Api;

public sealed record AbrirCasoRequest(string NumeroExpediente, string Descripcion);

public sealed record AsignarAnalistaRequest(string Usuario);

public sealed class OpcionesIngesta
{
    public const string Seccion = "Ingesta";

    /// <summary>Tope de tamaño por evidencia. Por defecto 20 GiB (exportaciones de video largas).</summary>
    public long TamanoMaximoBytes { get; set; } = 20L * 1024 * 1024 * 1024;
}

internal static class Endpoints
{
    public static void MapEndpointsForenses(this IEndpointRouteBuilder app)
    {
        var casos = app.MapGroup("/casos").WithTags("Casos");

        casos.MapPost("/", async (AbrirCasoRequest req, ServicioDeCasos s, CancellationToken ct) =>
        {
            var caso = await s.AbrirAsync(req.NumeroExpediente, req.Descripcion, ct);
            return Results.Created($"/casos/{caso.Id}", caso);
        });
        casos.MapGet("/", (ServicioDeCasos s, CancellationToken ct) => s.ListarAsync(ct));
        casos.MapGet("/{casoId:guid}", (Guid casoId, ServicioDeCasos s, CancellationToken ct) => s.ObtenerAsync(casoId, ct));
        casos.MapPost("/{casoId:guid}/analistas", (Guid casoId, AsignarAnalistaRequest req, ServicioDeCasos s, CancellationToken ct) =>
            s.AsignarAnalistaAsync(casoId, req.Usuario, ct));
        casos.MapPost("/{casoId:guid}/cerrar", (Guid casoId, ServicioDeCasos s, CancellationToken ct) => s.CerrarAsync(casoId, ct));

        // Ingesta: el cuerpo es el archivo en crudo (application/octet-stream), sin buffering en memoria.
        casos.MapPost("/{casoId:guid}/evidencias", async (
            Guid casoId, string nombre, HttpContext http, ServicioDeEvidencias s,
            IOptions<OpcionesIngesta> opciones, CancellationToken ct) =>
        {
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite)
                limite.MaxRequestBodySize = opciones.Value.TamanoMaximoBytes;

            var evidencia = await s.IngestarAsync(casoId, nombre, http.Request.Body, ct);
            return Results.Created($"/casos/{casoId}/evidencias/{evidencia.Id}", evidencia);
        });
        casos.MapGet("/{casoId:guid}/evidencias", (Guid casoId, ServicioDeEvidencias s, CancellationToken ct) =>
            s.ListarAsync(casoId, ct));
        casos.MapGet("/{casoId:guid}/evidencias/{evidenciaId:guid}/contenido", async (
            Guid casoId, Guid evidenciaId, HttpContext http, ServicioDeEvidencias s, CancellationToken ct) =>
        {
            var (evidencia, contenido) = await s.AbrirAsync(casoId, evidenciaId, ct);
            http.Response.Headers["X-Evidencia-Sha256"] = evidencia.Hash;
            return Results.Stream(contenido, "application/octet-stream", evidencia.NombreOriginal);
        });

        casos.MapGet("/{casoId:guid}/custodia", (Guid casoId, ServicioDeCustodia s, CancellationToken ct) =>
            s.ExportarAsync(casoId, ct));
    }
}
