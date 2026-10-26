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

/// <summary>Reducción de ruido por medias no locales. hColor sólo actúa sobre imágenes en color.</summary>
public sealed partial class FormularioDeDenoiseNlMeans : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _h = 10;

    [ObservableProperty]
    private decimal? _hColor = 10;

    [ObservableProperty]
    private decimal? _plantilla = 7;

    [ObservableProperty]
    private decimal? _busqueda = 21;

    public override string Tipo => "opencv.denoise.nlmeans/v1";
    public override string Titulo => "Reducción de ruido (NL-means)";

    public override object Parametros(MarcasDelVisor marcas) => new
    {
        h = Entero(H, "la fuerza h"),
        hColor = Entero(HColor, "la fuerza de color"),
        plantilla = Entero(Plantilla, "la ventana de plantilla"),
        busqueda = Entero(Busqueda, "la ventana de búsqueda"),
    };
}

/// <summary>Filtro de mediana con ventana impar.</summary>
public sealed partial class FormularioDeDenoiseMediana : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _ventana = 5;

    public override string Tipo => "opencv.denoise.mediana/v1";
    public override string Titulo => "Reducción de ruido (mediana)";

    public override object Parametros(MarcasDelVisor marcas) => new { ventana = Entero(Ventana, "la ventana") };
}

/// <summary>Máscara de enfoque: sigma del desenfoque y cantidad, con hasta dos decimales.</summary>
public sealed partial class FormularioDeUnsharp : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _sigma = 1.5m;

    [ObservableProperty]
    private decimal? _cantidad = 0.8m;

    public override string Tipo => "opencv.realce.unsharp/v1";
    public override string Titulo => "Realce (máscara de enfoque)";

    public override object Parametros(MarcasDelVisor marcas) => new
    {
        sigmaCentesimas = PuntoFijo.AEntero(Sigma, 2, "la sigma"),
        cantidadCentesimas = PuntoFijo.AEntero(Cantidad, 2, "la cantidad"),
    };
}

/// <summary>CLAHE: límite de contraste con un decimal y celdas por lado de la cuadrícula.</summary>
public sealed partial class FormularioDeClahe : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _limite = 2.0m;

    [ObservableProperty]
    private decimal? _cuadricula = 8;

    public override string Tipo => "opencv.realce.clahe/v1";
    public override string Titulo => "Realce (CLAHE)";

    public override object Parametros(MarcasDelVisor marcas) => new
    {
        limiteDecimas = PuntoFijo.AEntero(Limite, 1, "el límite"),
        cuadricula = Entero(Cuadricula, "la cuadrícula"),
    };
}

/// <summary>Brillo y contraste lineales: contraste con hasta dos decimales y brillo entero.</summary>
public sealed partial class FormularioDeBrilloContraste : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _contraste = 1.0m;

    [ObservableProperty]
    private decimal? _brillo = 0;

    public override string Tipo => "opencv.tonal.brillo-contraste/v1";
    public override string Titulo => "Brillo y contraste";

    public override object Parametros(MarcasDelVisor marcas) => new
    {
        contrasteCentesimas = PuntoFijo.AEntero(Contraste, 2, "el contraste"),
        brillo = Entero(Brillo, "el brillo"),
    };
}

/// <summary>Corrección gamma con hasta tres decimales (γ &gt; 1 aclara).</summary>
public sealed partial class FormularioDeGamma : FormularioDeOperacion
{
    [ObservableProperty]
    private decimal? _gamma = 1.0m;

    public override string Tipo => "opencv.tonal.gamma/v1";
    public override string Titulo => "Gamma";

    public override object Parametros(MarcasDelVisor marcas) => new { gammaMilesimas = PuntoFijo.AEntero(Gamma, 3, "la gamma") };
}

/// <summary>Ecualización del histograma (en color, sobre la luminancia). Sin parámetros.</summary>
public sealed class FormularioDeEcualizacion : FormularioDeOperacion
{
    public override string Tipo => "opencv.tonal.ecualizacion/v1";
    public override string Titulo => "Ecualización del histograma";

    public override object Parametros(MarcasDelVisor marcas) => new { };
}
