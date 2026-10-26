using CommunityToolkit.Mvvm.ComponentModel;

namespace Forensic.Cliente.Nucleo.Operaciones;

public static class Interpolaciones
{
    public static IReadOnlyList<string> Todas { get; } = ["vecino", "lineal", "cubica"];
}

/// <summary>El recorte usa la región marcada en el visor.</summary>
public sealed class FormularioDeRecorte : FormularioDeOperacion
{
    public override string Tipo => "opencv.recorte/v1";
    public override string Titulo => "Recorte";
    public override ModoDeMarcado Marcado => ModoDeMarcado.Region;

    public override object Parametros(MarcasDelVisor marcas) =>
        marcas.Region is { } r
            ? new { x = r.X, y = r.Y, ancho = r.Ancho, alto = r.Alto }
            : throw new ErrorDeFormulario("Marcá en la imagen la región a recortar.");
}

/// <summary>
/// Rectificación de perspectiva: 4 puntos marcados en el visor, en orden (superior izquierdo,
/// superior derecho, inferior derecho, inferior izquierdo), y el tamaño del rectángulo de salida.
/// </summary>
public sealed partial class FormularioDePerspectiva : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _ancho;

    [ObservableProperty]
    private decimal? _alto;

    [ObservableProperty]
    private string _interpolacion = "lineal";

    public override string Tipo => "opencv.perspectiva/v1";
    public override string Titulo => "Perspectiva";
    public override ModoDeMarcado Marcado => ModoDeMarcado.Puntos;

    public override object Parametros(MarcasDelVisor marcas)
    {
        if (marcas.Puntos.Count != 4)
            throw new ErrorDeFormulario(
                "Marcá en la imagen los 4 puntos, en orden: superior izquierdo, superior derecho, inferior derecho, inferior izquierdo.");
        var p = marcas.Puntos;
        return new
        {
            x1 = p[0].X, y1 = p[0].Y, x2 = p[1].X, y2 = p[1].Y, x3 = p[2].X, y3 = p[2].Y, x4 = p[3].X, y4 = p[3].Y,
            ancho = Entero(Ancho, "el ancho de salida"),
            alto = Entero(Alto, "el alto de salida"),
            interpolacion = Interpolacion,
        };
    }
}
