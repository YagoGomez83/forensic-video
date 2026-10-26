using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

namespace Forensic.Cliente.Tests.Vistas;

/// <summary>
/// El visor tiene que mostrar la imagen con la misma grilla de píxeles que ve el worker, que ignora la
/// orientación EXIF. Si Avalonia la aplicara, un ROI marcado en pantalla caería en otros píxeles.
/// </summary>
public class DecodificacionTests
{
    internal static Stream Recurso(string nombre) =>
        typeof(DecodificacionTests).Assembly.GetManifestResourceStream($"Forensic.Cliente.Tests.Recursos.{nombre}")
        ?? throw new InvalidOperationException($"Falta el recurso {nombre}.");

    [AvaloniaFact]
    public void Avalonia_no_aplica_la_orientacion_exif_como_tampoco_lo_hace_el_worker()
    {
        // 40x20 almacenado, con orientación EXIF 6 (rotar 90°): aplicada daría 20x40.
        using var bitmap = new Bitmap(Recurso("orientacion6.jpg"));

        Assert.Equal(new PixelSize(40, 20), bitmap.PixelSize);
    }
}
