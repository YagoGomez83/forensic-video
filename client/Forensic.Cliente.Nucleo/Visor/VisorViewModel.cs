using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Visor;

/// <summary>
/// Muestra una evidencia tal como la entrega el servidor (verificada y asentada como acceso). El
/// contenido queda en memoria. La vista decodifica sólo para mostrar e informa las dimensiones; la
/// traducción pantalla↔imagen vive en <see cref="TransformacionDeVista"/>.
/// </summary>
public sealed partial class VisorViewModel(ClienteForense api, INavegacion navegacion, ObservableObject? volverA, EvidenciaDto evidencia)
    : ObservableObject
{
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

    [RelayCommand]
    private void Volver()
    {
        if (volverA is not null)
            navegacion.IrA(volverA);
        else
            navegacion.IrACasos();
    }
}
