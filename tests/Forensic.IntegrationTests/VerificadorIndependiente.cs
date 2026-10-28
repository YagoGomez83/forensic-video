using System.Diagnostics;
using System.Text.Json;
using DotNet.Testcontainers.Builders;

namespace Forensic.IntegrationTests;

/// <summary>
/// El verificador de terceros (tools/verificador, en Go), compilado una vez por proceso y corrido como
/// lo correría un perito de contraparte: un ejecutable aparte, sin nada del servidor.
/// </summary>
internal static class VerificadorIndependiente
{
    private static readonly Lazy<Task<string>> Binario = new(CompilarAsync);

    public sealed record Corrida(int Codigo, JsonElement Resultado, string Salida)
    {
        public string Estado => Resultado.GetProperty("estado").GetString()!;

        public IEnumerable<string> Fallas => Resultado.GetProperty("comprobaciones").EnumerateArray()
            .Where(c => c.GetProperty("nivel").GetString() == "FALLA")
            .Select(c => c.GetProperty("mensaje").GetString()!);
    }

    public static async Task<Corrida> VerificarAsync(string bundle, string huella, string? externos = null, string? exigir = null)
    {
        var args = new List<string> { "--json", "--clave", huella };
        if (externos is not null)
            args.AddRange(["--externos", externos]);
        if (exigir is not null)
            args.AddRange(["--exigir", exigir]);
        args.Add(bundle);

        var (codigo, salida, error) = await CorrerAsync(await Binario.Value, args, workingDirectory: null);
        if (codigo == 3)
            throw new InvalidOperationException($"Error de uso del verificador: {salida}{error}");
        using var doc = JsonDocument.Parse(salida);
        return new Corrida(codigo, doc.RootElement.Clone(), salida);
    }

    /// <summary>La salida de texto, tal como la lee un perito en su consola.</summary>
    public static async Task<(int Codigo, string Salida)> VerificarEnTextoAsync(string bundle, string huella, string exigir)
    {
        var (codigo, salida, error) = await CorrerAsync(await Binario.Value, ["--clave", huella, "--exigir", exigir, bundle], workingDirectory: null);
        return (codigo, salida + error);
    }

    private static async Task<string> CompilarAsync()
    {
        var fuente = Path.Combine(CommonDirectoryPath.GetGitDirectory().DirectoryPath, "tools", "verificador");
        var destino = Path.Combine(Path.GetTempPath(), "forensic-video-tests", $"verificador-{Guid.NewGuid():N}{(OperatingSystem.IsWindows() ? ".exe" : "")}");
        var (codigo, salida, error) = await CorrerAsync("go", ["build", "-trimpath", "-o", destino, "."], fuente, ("CGO_ENABLED", "0"));
        if (codigo != 0)
            throw new InvalidOperationException($"No se pudo compilar el verificador (¿está Go instalado?): {salida}{error}");
        return destino;
    }

    private static async Task<(int, string, string)> CorrerAsync(
        string programa, IEnumerable<string> args, string? workingDirectory, params (string, string)[] entorno)
    {
        var info = new ProcessStartInfo(programa)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // El verificador escribe UTF-8; sin esto, en Windows se leería con la página de códigos de la consola.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var a in args)
            info.ArgumentList.Add(a);
        foreach (var (clave, valor) in entorno)
            info.Environment[clave] = valor;
        using var proceso = Process.Start(info)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var error = proceso.StandardError.ReadToEndAsync();
        await proceso.WaitForExitAsync();
        return (proceso.ExitCode, await salida, await error);
    }
}
