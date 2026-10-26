using System.Net;
using System.Text;
using System.Text.Json;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Tests.Dobles;

/// <summary>Servidor HTTP falso: responde según la ruta y registra cada petición que recibe.</summary>
internal sealed class ServidorFalso : HttpMessageHandler
{
    private readonly List<(HttpMethod Metodo, string Ruta, Func<HttpRequestMessage, HttpResponseMessage> Responder)> _rutas = [];

    public List<HttpRequestMessage> Peticiones { get; } = [];

    public bool Inalcanzable { get; set; }

    public ServidorFalso En(HttpMethod metodo, string ruta, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _rutas.Add((metodo, ruta, responder));
        return this;
    }

    public ServidorFalso En(HttpMethod metodo, string ruta, HttpStatusCode estado, object? cuerpo = null) =>
        En(metodo, ruta, _ => Respuesta(estado, cuerpo));

    public static HttpResponseMessage Respuesta(HttpStatusCode estado, object? cuerpo = null) => new(estado)
    {
        Content = cuerpo is null ? null : new StringContent(JsonSerializer.Serialize(cuerpo, ClienteForense.Json), Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Problema(HttpStatusCode estado, string titulo, string detalle) => new(estado)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { type = "about:blank", title = titulo, status = (int)estado, detail = detalle }),
            Encoding.UTF8, "application/problem+json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        if (Inalcanzable)
            throw new HttpRequestException("No se puede establecer una conexión.", new System.Net.Sockets.SocketException(10061));
        // Se consume el cuerpo para que los tests puedan inspeccionarlo después.
        if (peticion.Content is not null)
            await peticion.Content.LoadIntoBufferAsync();
        Peticiones.Add(peticion);
        var ruta = peticion.RequestUri!.PathAndQuery;
        foreach (var (metodo, patron, responder) in _rutas)
            if (metodo == peticion.Method && (patron == ruta || (patron.EndsWith('*') && ruta.StartsWith(patron[..^1], StringComparison.Ordinal))))
                return responder(peticion);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
