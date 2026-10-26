using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forensic.Cliente.Api;

/// <summary>
/// El único punto de contacto con el servidor de custodia. Agrega el JWT de la sesión a cada
/// petición y, ante un 401 de una sesión activa, la expira (la UI vuelve al login).
/// </summary>
internal sealed class ManejadorDeSesion(ISesion sesion) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> ConToken = new("forensic.conToken");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        var token = sesion.Token;
        if (token is not null)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        peticion.Options.Set(ConToken, token is not null);

        var respuesta = await base.SendAsync(peticion, ct);
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized && token is not null)
            sesion.Expirar();
        return respuesta;
    }
}

/// <summary>
/// Cliente tipado de la API REST. No tiene lógica forense: no calcula hashes ni decide nada
/// pericial; manda pedidos y traduce respuestas.
/// </summary>
public sealed class ClienteForense
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;
    private readonly ISesion _sesion;

    private ClienteForense(HttpClient http, ISesion sesion)
    {
        _http = http;
        _sesion = sesion;
    }

    public ISesion Sesion => _sesion;
    public Uri Servidor => _http.BaseAddress!;

    /// <param name="interno">Para tests; por defecto, el handler de sockets de .NET.</param>
    public static ClienteForense Crear(Uri servidor, ISesion sesion, HttpMessageHandler? interno = null)
    {
        var http = new HttpClient(new ManejadorDeSesion(sesion) { InnerHandler = interno ?? new SocketsHttpHandler() })
        {
            BaseAddress = servidor,
            // Las subidas y descargas de evidencia pueden ser largas; los cortes los maneja el usuario.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new ClienteForense(http, sesion);
    }

    public async Task<EstadoDeInicio> IniciarSesionAsync(string usuario, string contrasena, CancellationToken ct)
    {
        var token = await EnviarAsync<TokenEmitido>(HttpMethod.Post, "auth/login", new { usuario, contrasena }, ct);
        _sesion.Iniciar(token.Token, token.ExpiraEn);
        return await RequiereCambioDeContrasenaAsync(ct) ? EstadoDeInicio.DebeCambiarContrasena : EstadoDeInicio.Listo;
    }

    /// <summary>Cambio de la propia contraseña. El servidor devuelve un token nuevo, que reemplaza al anterior.</summary>
    public async Task CambiarContrasenaAsync(string actual, string nueva, CancellationToken ct)
    {
        var token = await EnviarAsync<TokenEmitido>(HttpMethod.Post, "auth/contrasena", new { actual, nueva }, ct);
        _sesion.Iniciar(token.Token, token.ExpiraEn);
    }

    /// <summary>
    /// La respuesta del login no dice si la contraseña es temporal (decisión (b) de la Fase 3, sin tocar
    /// el backend). Con una contraseña temporal el servidor sólo permite cambiarla, así que GET /casos,
    /// que cualquier sesión completa puede pedir, responde 403. Si el login empieza a informarlo, sólo
    /// cambia este método.
    /// </summary>
    private async Task<bool> RequiereCambioDeContrasenaAsync(CancellationToken ct)
    {
        try
        {
            await ListarCasosAsync(ct);
            return false;
        }
        catch (ErrorDeApi ex) when (ex.Tipo == TipoDeError.AccesoDenegado)
        {
            return true;
        }
    }

    public Task<IReadOnlyList<CasoDto>> ListarCasosAsync(CancellationToken ct) =>
        EnviarAsync<IReadOnlyList<CasoDto>>(HttpMethod.Get, "casos", null, ct);

    public Task<CasoDto> AbrirCasoAsync(string numeroExpediente, string descripcion, CancellationToken ct) =>
        EnviarAsync<CasoDto>(HttpMethod.Post, "casos", new { numeroExpediente, descripcion }, ct);

    public Task<IReadOnlyList<EvidenciaDto>> ListarEvidenciasAsync(Guid casoId, CancellationToken ct) =>
        EnviarAsync<IReadOnlyList<EvidenciaDto>>(HttpMethod.Get, $"casos/{casoId}/evidencias", null, ct);

    /// <summary>
    /// Sube una evidencia original en crudo. El servidor la hashea y la custodia; el cliente no
    /// calcula nada. <paramref name="progreso"/> recibe los bytes enviados hasta el momento.
    /// </summary>
    public async Task<EvidenciaDto> IngestarAsync(
        Guid casoId, string nombre, Stream contenido, long tamanoBytes, Action<long>? progreso, CancellationToken ct)
    {
        var cuerpo = new ContenidoConProgreso(contenido, tamanoBytes, progreso);
        cuerpo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"casos/{casoId}/evidencias?nombre={Uri.EscapeDataString(nombre)}")
        {
            Content = cuerpo,
        };
        using var respuesta = await EnviarAsync(peticion, ct);
        return (await respuesta.Content.ReadFromJsonAsync<EvidenciaDto>(Json, ct))!;
    }

    /// <summary>La cadena de custodia del caso con la verificación que hace el servidor.</summary>
    public Task<ExportDeCustodia> ExportarCustodiaAsync(Guid casoId, CancellationToken ct) =>
        EnviarAsync<ExportDeCustodia>(HttpMethod.Get, $"casos/{casoId}/custodia", null, ct);

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null)
            peticion.Content = JsonContent.Create(cuerpo, options: Json);
        using var respuesta = await EnviarAsync(peticion, ct);
        return (await respuesta.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    /// <summary>Envía y devuelve la respuesta si fue exitosa; si no, lanza el <see cref="ErrorDeApi"/> que corresponde.</summary>
    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage peticion, CancellationToken ct, HttpCompletionOption modo = HttpCompletionOption.ResponseContentRead)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, modo, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ErrorDeApi(TipoDeError.ServidorInalcanzable, null,
                $"No se pudo conectar con el servidor {Servidor}. Verificá la red y que el servidor esté en marcha.", ex);
        }
        if (respuesta.IsSuccessStatusCode)
            return respuesta;

        using (respuesta)
        {
            var conToken = peticion.Options.TryGetValue(ManejadorDeSesion.ConToken, out var t) && t;
            var detalle = await DetalleAsync(respuesta, ct);
            throw respuesta.StatusCode switch
            {
                HttpStatusCode.Unauthorized when conToken => new SesionExpiradaException(detalle ?? "La sesión venció. Volvé a iniciar sesión."),
                HttpStatusCode.Unauthorized => new ErrorDeApi(TipoDeError.CredencialesInvalidas, respuesta.StatusCode, detalle ?? "No autenticado."),
                _ => new ErrorDeApi(Clasificar(respuesta.StatusCode), respuesta.StatusCode, detalle ?? $"El servidor respondió {(int)respuesta.StatusCode}."),
            };
        }
    }

    private static TipoDeError Clasificar(HttpStatusCode estado) => estado switch
    {
        HttpStatusCode.Forbidden => TipoDeError.AccesoDenegado,
        HttpStatusCode.NotFound => TipoDeError.NoEncontrado,
        HttpStatusCode.Conflict => TipoDeError.Conflicto,
        HttpStatusCode.UnprocessableEntity => TipoDeError.Rechazado,
        HttpStatusCode.Locked => TipoDeError.IntegridadComprometida,
        HttpStatusCode.TooManyRequests => TipoDeError.DemasiadosIntentos,
        HttpStatusCode.BadGateway => TipoDeError.DivergenciaDeSeguridad,
        HttpStatusCode.ServiceUnavailable => TipoDeError.WorkerNoDisponible,
        _ => TipoDeError.ErrorDelServidor,
    };

    /// <summary>El <c>detail</c> (o el <c>title</c>) del ProblemDetails, tal como lo escribió el servidor.</summary>
    private static async Task<string?> DetalleAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var problema = await respuesta.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            foreach (var campo in new[] { "detail", "title" })
                if (problema.ValueKind == JsonValueKind.Object && problema.TryGetProperty(campo, out var v) && v.GetString() is { Length: > 0 } texto)
                    return texto;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
        }
        return null;
    }
}
