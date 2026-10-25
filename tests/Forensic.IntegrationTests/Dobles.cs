using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Forensic.IntegrationTests;

/// <summary>Captura el log técnico del servidor para verificar qué se registró y con qué severidad.</summary>
internal sealed class RegistroDeLogs : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Nivel, string Categoria, string Mensaje)> Entradas { get; } = new();

    public bool HayCriticos => Entradas.Any(e => e.Nivel == LogLevel.Critical);

    public ILogger CreateLogger(string categoria) => new Logger(this, categoria);

    public void Dispose()
    {
    }

    private sealed class Logger(RegistroDeLogs registro, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel nivel) => true;

        public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex, Func<TState, Exception?, string> formato) =>
            registro.Entradas.Enqueue((nivel, categoria, formato(estado, ex)));
    }
}

/// <summary>Worker HTTP falso: consume el cuerpo y responde siempre lo mismo.</summary>
internal sealed class RespuestaFijaDelWorker(HttpStatusCode status, string cuerpo) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        if (solicitud.Content is not null)
            await solicitud.Content.CopyToAsync(Stream.Null, ct);
        return new HttpResponseMessage(status) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
    }
}
