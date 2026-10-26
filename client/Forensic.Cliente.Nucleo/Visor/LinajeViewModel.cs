using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Visor;

/// <summary>
/// El historial de una evidencia y su reproducción. Reproducir lo hace el servidor (re-ejecuta desde el
/// asiento y compara byte a byte); acá sólo se pide y se muestra su veredicto.
/// </summary>
public sealed partial class LinajeViewModel(ClienteForense api, INavegacion navegacion, ObservableObject? volverA, EvidenciaDto evidencia)
    : ObservableObject
{
    public EvidenciaDto Evidencia { get; } = evidencia;

    public ObservableCollection<EslabonDeLinaje> Eslabones { get; } = [];

    [ObservableProperty]
    private bool? _reproducido;

    [ObservableProperty]
    private string? _veredicto;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _reproduciendo;

    [RelayCommand]
    private async Task CargarAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            var eslabones = await api.ObtenerLinajeAsync(Evidencia.CasoId, Evidencia.Id, ct);
            Eslabones.Clear();
            foreach (var eslabon in eslabones)
                Eslabones.Add(eslabon);
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

    /// <summary>Un original no salió de ninguna operación: no hay nada que reproducir.</summary>
    private bool EsDerivado() => Evidencia.PadreId is not null;

    [RelayCommand(CanExecute = nameof(EsDerivado))]
    private async Task ReproducirAsync(CancellationToken ct)
    {
        (Error, Veredicto, Reproducido, Reproduciendo) = (null, null, null, true);
        try
        {
            var r = await api.ReproducirAsync(Evidencia.CasoId, Evidencia.Id, ct);
            Reproducido = r.Reproducido;
            var entorno = r.EntornoCoincide ? "Entorno igual al asentado." : "El entorno del worker es distinto del asentado.";
            Veredicto = r.Reproducido
                ? $"Se reprodujo byte a byte: SHA-256 {r.Sha256Obtenido}. {entorno}"
                : $"NO se reprodujo: se esperaba {r.Sha256Esperado} y se obtuvo {r.Sha256Obtenido}. {entorno}";
            await CargarAsync(ct);
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
            Reproduciendo = false;
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
