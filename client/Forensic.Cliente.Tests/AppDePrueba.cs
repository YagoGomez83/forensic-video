using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Forensic.Cliente.Tests.AppDePrueba))]

namespace Forensic.Cliente.Tests;

/// <summary>La app real (sus vistas, estilos y localizador) sobre la plataforma headless: sin pantalla.</summary>
public static class AppDePrueba
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
