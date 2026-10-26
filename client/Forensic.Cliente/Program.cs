using Avalonia;

namespace Forensic.Cliente;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        ConstruirApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder ConstruirApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
