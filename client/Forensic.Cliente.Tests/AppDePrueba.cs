using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Forensic.Cliente.Tests.AppDePrueba))]

namespace Forensic.Cliente.Tests;

/// <summary>
/// La app real (sus vistas, estilos y localizador) sobre la plataforma headless, sin pantalla, pero
/// con Skia de verdad: el decodificador de imágenes es el mismo que usa la app.
/// </summary>
public static class AppDePrueba
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
