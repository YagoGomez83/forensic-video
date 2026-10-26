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

/// <summary>Rotación exacta en cuartos de vuelta, en sentido horario.</summary>
public sealed partial class FormularioDeRotacion90 : FormularioDeOperacion
{
    public static IReadOnlyList<int> OpcionesDeGiros { get; } = [1, 2, 3];

    [ObservableProperty]
    private int _giros = 1;

    public override string Tipo => "opencv.rotacion90/v1";
    public override string Titulo => "Rotación exacta (90°, 180°, 270°)";

    public override object Parametros(MarcasDelVisor marcas) => new { giros = Giros };
}

/// <summary>
/// Rotación por ángulo alrededor del centro, en grados con hasta dos decimales (el servidor los recibe
/// en centésimas). Positivo = antihorario, como en OpenCV.
/// </summary>
public sealed partial class FormularioDeRotacion : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _grados;

    [ObservableProperty]
    private string _interpolacion = "lineal";

    public override string Tipo => "opencv.rotacion/v1";
    public override string Titulo => "Rotación por ángulo";

    public override object Parametros(MarcasDelVisor marcas) =>
        new { centesimasDeGrado = PuntoFijo.AEntero(Grados, 2, "el ángulo"), interpolacion = Interpolacion };
}
