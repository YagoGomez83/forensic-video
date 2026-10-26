using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Forensic.Application;
using Forensic.Application.Operaciones;
using Forensic.Domain.Comun;

namespace Forensic.Infrastructure.Workers;

public sealed class OpcionesWorkerProcesamiento
{
    public const string Seccion = "WorkerProcesamiento";

    /// <summary>URL del worker de procesamiento, p. ej. <c>http://localhost:8001</c>.</summary>
    public Uri? UrlBase { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>Cliente de <c>POST /v1/operaciones</c> del worker de procesamiento (Python/OpenCV).</summary>
internal sealed class WorkerDeProcesamientoHttp(HttpClient http) : IWorkerDeProcesamiento
{
    public async Task<SalidaDeProcesamiento> AplicarAsync(string operacion, string parametros, Stream entrada, CancellationToken ct)
    {
        using var cuerpo = new MultipartFormDataContent
        {
            { new StringContent(operacion), "operacion" },
            { new StringContent(parametros), "parametros" },
        };
        var archivo = new StreamContent(entrada);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        cuerpo.Add(archivo, "entrada", "entrada");

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.PostAsync("v1/operaciones", cuerpo, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new WorkerDeProcesamientoNoDisponibleException($"El worker de procesamiento no respondió: {ex.Message}", ex);
        }

        try
        {
            if (respuesta.StatusCode == HttpStatusCode.UnprocessableEntity)
                throw new ProcesamientoRechazadoException(
                    $"El worker de procesamiento rechazó la operación: {await respuesta.Content.ReadAsStringAsync(ct)}");
            if (!respuesta.IsSuccessStatusCode)
                throw new WorkerDeProcesamientoNoDisponibleException($"El worker de procesamiento respondió {(int)respuesta.StatusCode}.");

            var hashEntrada = HashSha256.Parse(Cabecera(respuesta, "X-Sha256-Entrada"));
            var hashSalida = HashSha256.Parse(Cabecera(respuesta, "X-Sha256-Salida"));
            var hashPixeles = HashSha256.Parse(Cabecera(respuesta, "X-Sha256-Pixeles")).Hex;
            var entorno = Cabecera(respuesta, "X-Entorno");
            using (var doc = JsonDocument.Parse(entorno))
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("X-Entorno no es un objeto JSON.");

            var contenido = await respuesta.Content.ReadAsStreamAsync(ct);
            return new SalidaDeProcesamiento(contenido, hashEntrada, hashSalida, hashPixeles, entorno, respuesta);
        }
        catch (Exception ex) when (ex is JsonException or ExcepcionDeDominio or InvalidOperationException)
        {
            respuesta.Dispose();
            throw new WorkerDeProcesamientoNoDisponibleException($"Respuesta inválida del worker de procesamiento: {ex.Message}", ex);
        }
        catch
        {
            respuesta.Dispose();
            throw;
        }
    }

    private static string Cabecera(HttpResponseMessage respuesta, string nombre) =>
        respuesta.Headers.TryGetValues(nombre, out var valores) && valores.SingleOrDefault() is { Length: > 0 } valor
            ? valor
            : throw new InvalidOperationException($"Falta la cabecera {nombre}.");
}
