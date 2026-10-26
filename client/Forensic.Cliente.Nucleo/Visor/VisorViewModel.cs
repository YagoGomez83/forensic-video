using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Visor;

/// <summary>
/// Muestra una evidencia tal como la entrega el servidor (verificada y asentada como acceso). El
/// contenido queda en memoria. La vista decodifica sólo para mostrar e informa las dimensiones; la
/// traducción pantalla↔imagen vive en <see cref="TransformacionDeVista"/>.
/// </summary>
public sealed partial class VisorViewModel(
    ClienteForense api, INavegacion navegacion, ObservableObject? volverA, EvidenciaDto evidencia, string? operacionDeOrigen = null)
    : ObservableObject
{
    /// <summary>Para un derivado recién creado: la operación y sus parámetros tal como los asentó el servidor.</summary>
    public string? OperacionDeOrigen { get; } = operacionDeOrigen;

    /// <summary>La región marcada para recortar, en píxeles de la imagen original.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AplicarRecorteCommand))]
    private Region? _regionMarcada;

    [ObservableProperty]
    private bool _aplicando;

    /// <summary>Más que esto no se trae a memoria sólo para previsualizar.</summary>
    public const long TamanoMaximoDePrevisualizacion = 256L * 1024 * 1024;

    public EvidenciaDto Evidencia { get; } = evidencia;

    [ObservableProperty]
    private byte[]? _contenido;

    [ObservableProperty]
    private string? _sinPrevisualizacion;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private int _anchoImagen;

    [ObservableProperty]
    private int _altoImagen;

    [ObservableProperty]
    private TransformacionDeVista _transformacion = new();

    internal ClienteForense Api => api;
    internal INavegacion Navegacion => navegacion;

    [RelayCommand]
    private async Task CargarAsync(CancellationToken ct)
    {
        Error = null;
        if (Evidencia.TamanoBytes > TamanoMaximoDePrevisualizacion)
        {
            SinPrevisualizacion = $"La evidencia pesa {Evidencia.TamanoBytes / (1024 * 1024)} MB: es demasiado grande para previsualizarla.";
            return;
        }
        try
        {
            Contenido = await api.DescargarContenidoAsync(Evidencia.CasoId, Evidencia.Id, ct);
        }
        catch (SesionExpiradaException)
        {
            // La vuelta al login la hace VentanaViewModel con el evento de la sesión.
        }
        catch (ErrorDeApi ex)
        {
            Error = Mensajes.Para(ex);
        }
    }

    /// <summary>La vista pudo decodificar el contenido: éstas son las dimensiones de la imagen original.</summary>
    public void ImagenDecodificada(int ancho, int alto) => (AnchoImagen, AltoImagen, SinPrevisualizacion) = (ancho, alto, null);

    /// <summary>La vista no pudo decodificarlo (un video, un TIFF...): no se inventa ninguna imagen.</summary>
    public void NoDecodificable()
    {
        (AnchoImagen, AltoImagen) = (0, 0);
        SinPrevisualizacion = $"«{Evidencia.NombreOriginal}» no se puede previsualizar (p. ej. video o TIFF). " +
            "Las operaciones que no necesitan ver la imagen siguen disponibles.";
    }

    public void AjustarA(double anchoVista, double altoVista) =>
        Transformacion = TransformacionDeVista.Ajustada(AnchoImagen, AltoImagen, anchoVista, altoVista);

    public void ZoomEn(double factor, Punto cursor) => Transformacion = Transformacion.ZoomEn(factor, cursor);

    public void Desplazar(double dx, double dy) => Transformacion = Transformacion.Desplazada(dx, dy);

    /// <summary>Un arrastre en pantalla: la región se guarda ya traducida a píxeles de la imagen.</summary>
    public void MarcarRegion(Punto desde, Punto hasta) =>
        RegionMarcada = AnchoImagen > 0 ? Transformacion.RegionEnImagen(desde, hasta, AnchoImagen, AltoImagen) : null;

    private bool HayRegion() => RegionMarcada is not null;

    [RelayCommand(CanExecute = nameof(HayRegion))]
    private Task AplicarRecorteAsync(CancellationToken ct) =>
        RegionMarcada is { } r
            ? AplicarAsync("opencv.recorte/v1", new { x = r.X, y = r.Y, ancho = r.Ancho, alto = r.Alto }, ct)
            : Task.CompletedTask;

    /// <summary>
    /// Aplica de verdad en el servidor y abre el derivado que devolvió: no hay previsualización que
    /// pueda diferir del resultado custodiado.
    /// </summary>
    internal async Task AplicarAsync(string tipo, object parametros, CancellationToken ct)
    {
        (Error, Aplicando) = (null, true);
        try
        {
            var aplicada = await api.AplicarOperacionAsync(Evidencia.CasoId, Evidencia.Id, tipo, parametros, null, ct);
            navegacion.IrA(new VisorViewModel(api, navegacion, volverA, aplicada.Derivado, $"{aplicada.Tipo} {aplicada.Parametros}"));
        }
        catch (SesionExpiradaException)
        {
            // La vuelta al login la hace VentanaViewModel con el evento de la sesión.
        }
        catch (ErrorDeApi ex)
        {
            Error = Mensajes.Para(ex);
        }
        finally
        {
            Aplicando = false;
        }
    }

    [RelayCommand]
    private void Volver()
    {
        if (volverA is not null)
            navegacion.IrA(volverA);
        else
            navegacion.IrACasos();
    }
}
