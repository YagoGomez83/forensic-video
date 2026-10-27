using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Forensic.Cliente.Api;

/// <summary>
/// El worker de IA de esta workstation (Fase 4). Corre local, con la GPU del analista, y nunca fuera de
/// la máquina: sólo se acepta una URL de loopback. Lo que devuelve es «sólo investigación» y se reenvía
/// al servidor tal cual, sin interpretarlo (<see cref="ClienteForense.RegistrarAnalisisIaAsync"/>).
/// </summary>
public sealed class ClienteWorkerIa
{
    public static readonly Uri PorDefecto = new("http://127.0.0.1:8100/");

    private readonly HttpClient _http;

    private ClienteWorkerIa(HttpClient http) => _http = http;

    public Uri Worker => _http.BaseAddress!;

    public static ClienteWorkerIa Crear(Uri worker, HttpMessageHandler? interno = null)
    {
        if (!worker.IsLoopback)
            throw new ArgumentException(
                $"El worker de IA corre en esta workstation: '{worker}' no es una dirección local (127.0.0.1 o localhost).",
                nameof(worker));
        return new ClienteWorkerIa(new HttpClient(interno ?? new SocketsHttpHandler()) { BaseAddress = worker });
    }

    /// <summary>
    /// Detección de objetos sobre la imagen en crudo. Devuelve la respuesta del worker como texto, sin
    /// deserializarla: es lo que se reporta al servidor.
    /// </summary>
    public async Task<string> DetectarAsync(byte[] imagen, int umbralConfianzaMilesimas, CancellationToken ct)
    {
        using var respuesta = await EnviarAsync("v1/detectar", imagen, ConUmbral(umbralConfianzaMilesimas), ct);
        return await respuesta.Content.ReadAsStringAsync(ct);
    }

    /// <summary>OCR sobre la imagen en crudo, con los mismos parámetros que la detección. Respuesta tal cual.</summary>
    public async Task<string> LeerTextoAsync(byte[] imagen, int umbralConfianzaMilesimas, CancellationToken ct)
    {
        using var respuesta = await EnviarAsync("v1/ocr", imagen, ConUmbral(umbralConfianzaMilesimas), ct);
        return await respuesta.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Super-resolución x4: el derivado PNG y su reporte (la cabecera X-Reporte), los dos tal cual. Sin
    /// reporte el derivado no tiene procedencia y no se acepta.
    /// </summary>
    public async Task<(byte[] Png, string Reporte)> SuperResolverAsync(byte[] imagen, CancellationToken ct)
    {
        using var respuesta = await EnviarAsync("v1/superresolucion", imagen, "{}", ct);
        if (!respuesta.Headers.TryGetValues("X-Reporte", out var reporte))
            throw new ErrorDeApi(TipoDeError.WorkerNoDisponible, respuesta.StatusCode,
                "El worker de IA devolvió un derivado sin su reporte (X-Reporte): no tiene procedencia y no se registra.");
        return (await respuesta.Content.ReadAsByteArrayAsync(ct), reporte.Single());
    }

    private static string ConUmbral(int umbralConfianzaMilesimas) =>
        $$"""{"umbralConfianzaMilesimas":{{umbralConfianzaMilesimas}}}""";

    /// <summary>Manda la imagen en crudo con sus parámetros; devuelve la respuesta sólo si fue exitosa.</summary>
    private async Task<HttpResponseMessage> EnviarAsync(string ruta, byte[] imagen, string parametros, CancellationToken ct)
    {
        var entrada = new ByteArrayContent(imagen);
        entrada.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(parametros, Encoding.UTF8), "parametros" },
            { entrada, "entrada", "entrada" },
        };

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.PostAsync(ruta, formulario, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ErrorDeApi(TipoDeError.WorkerNoDisponible, null,
                $"No se pudo conectar con el worker de IA local en {Worker}. Verificá que esté en marcha " +
                "(docker compose --profile workstation up -d worker-ia).", ex);
        }

        if (respuesta.IsSuccessStatusCode)
            return respuesta;
        using (respuesta)
        {
            var detalle = await ClienteForense.DetalleAsync(respuesta, ct);
            throw respuesta.StatusCode == HttpStatusCode.UnprocessableEntity
                ? new ErrorDeApi(TipoDeError.Rechazado, respuesta.StatusCode, detalle ?? "El worker de IA rechazó la imagen.")
                : new ErrorDeApi(TipoDeError.WorkerNoDisponible, respuesta.StatusCode,
                    $"El worker de IA respondió {(int)respuesta.StatusCode}{(detalle is null ? "." : $": {detalle}")}");
        }
    }
}
