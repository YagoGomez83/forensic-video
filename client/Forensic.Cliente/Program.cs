using System.Runtime.InteropServices;
using Avalonia;
using Forensic.Cliente.Api;

namespace Forensic.Cliente;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is [Comprobacion.Argumento])
            return Comprobar();
        return ConstruirApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder ConstruirApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static int Comprobar()
    {
        // Es una aplicación de ventana: para escribir en la consola desde la que se la llamó hay que
        // engancharse a ella (si la salida está redirigida, ya va al archivo o al pipe).
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
            AttachConsole(ProcesoPadre);
        return Comprobacion.EjecutarAsync(
            ConfiguracionDeWorkstation.RutaPorDefecto, Environment.GetEnvironmentVariable, Console.Out, CanalSeguro.Para,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private const int ProcesoPadre = -1;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int idDeProceso);
}
