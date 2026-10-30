using Forensic.Cliente.Api;

namespace Forensic.Cliente;

/// <summary>
/// <c>Forensic.Cliente.exe --comprobar</c>: valida la configuración de la workstation, carga la CA y el
/// certificado y hace una petición al servidor, sin abrir la ventana. Cualquier respuesta HTTP (sin
/// sesión, un 401) prueba que el canal funciona: con https, que el servidor aceptó el certificado de la
/// workstation (de la CA y no revocado) y que la workstation aceptó el del servidor.
/// </summary>
internal static class Comprobacion
{
    public const string Argumento = "--comprobar";

    public const int Correcta = 0;
    public const int ConfiguracionInvalida = 1;
    public const int ServidorInalcanzable = 2;

    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(15);

    public static async Task<int> EjecutarAsync(
        string rutaConfiguracion, Func<string, string?> variable, TextWriter salida,
        Func<ConfiguracionDeWorkstation, HttpMessageHandler?> canal, CancellationToken ct)
    {
        ConfiguracionDeWorkstation config;
        HttpMessageHandler? manejador;
        try
        {
            config = ConfiguracionDeWorkstation.Cargar(rutaConfiguracion, variable);
            salida.WriteLine($"Configuración: {config.Origen}");
            salida.WriteLine($"Servidor:      {config.Servidor}");
            salida.WriteLine($"Worker de IA:  {config.WorkerIa}");
            manejador = canal(config);
        }
        catch (Exception ex)
        {
            salida.WriteLine($"[FALLA] {ex.Message}");
            return ConfiguracionInvalida;
        }

        using var http = new HttpClient(manejador ?? new SocketsHttpHandler()) { BaseAddress = config.Servidor, Timeout = Espera };
        try
        {
            using var respuesta = await http.GetAsync("casos", ct);
            salida.WriteLine($"[OK] El servidor respondió ({(int)respuesta.StatusCode}).");
            return Correcta;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Un TLS rechazado (certificado revocado, otra CA, otro nombre) viene como causa interna.
            var motivos = new List<string>();
            for (Exception? e = ex; e is not null; e = e.InnerException)
                motivos.Add(e.Message);
            salida.WriteLine($"[FALLA] No se pudo hablar con {config.Servidor}: {string.Join(" / ", motivos.Distinct())}");
            return ServidorInalcanzable;
        }
    }
}
