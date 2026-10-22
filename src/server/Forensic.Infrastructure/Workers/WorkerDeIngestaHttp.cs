using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Forensic.Application;
using Forensic.Domain.Comun;

namespace Forensic.Infrastructure.Workers;

public sealed class OpcionesWorkerIngesta
{
    public const string Seccion = "WorkerIngesta";

    /// <summary>URL del worker de ingesta, p. ej. <c>http://localhost:8000</c>.</summary>
    public Uri? UrlBase { get; set; }

    /// <summary>Tope por análisis. Incluye transferir la evidencia completa al worker.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Cliente de <c>POST /v1/analizar</c> del worker de ingesta (Python).</summary>
internal sealed class WorkerDeIngestaHttp(HttpClient http) : IWorkerDeIngesta
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Respuesta(string Sha256, long TamanoBytes, JsonElement? Ffprobe, string? MotivoSinMetadatos);

    public async Task<AnalisisDeIngesta> AnalizarAsync(Stream contenido, CancellationToken ct)
    {
        using var cuerpo = new StreamContent(contenido);
        cuerpo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.PostAsync("v1/analizar", cuerpo, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new WorkerDeIngestaNoDisponibleException($"El worker de ingesta no respondió: {ex.Message}", ex);
        }

        using (respuesta)
        {
            if (respuesta.StatusCode == HttpStatusCode.UnprocessableEntity)
                throw new IngestaRechazadaException(
                    $"El worker de ingesta rechazó el contenido: {await respuesta.Content.ReadAsStringAsync(ct)}");
            if (!respuesta.IsSuccessStatusCode)
                throw new WorkerDeIngestaNoDisponibleException($"El worker de ingesta respondió {(int)respuesta.StatusCode}.");

            try
            {
                var r = await respuesta.Content.ReadFromJsonAsync<Respuesta>(Json, ct)
                    ?? throw new JsonException("Respuesta vacía.");
                return new AnalisisDeIngesta(HashSha256.Parse(r.Sha256), r.TamanoBytes, r.Ffprobe, r.MotivoSinMetadatos);
            }
            catch (Exception ex) when (ex is JsonException or ExcepcionDeDominio)
            {
                throw new WorkerDeIngestaNoDisponibleException($"Respuesta inválida del worker de ingesta: {ex.Message}", ex);
            }
        }
    }
}
