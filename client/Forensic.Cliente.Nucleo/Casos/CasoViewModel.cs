using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Casos;

/// <summary>
/// Un caso: sus evidencias y su cadena de custodia. La integridad de la cadena es el veredicto del
/// servidor; el cliente no recalcula ningún hash.
/// </summary>
public sealed partial class CasoViewModel(ClienteForense api, INavegacion navegacion, CasoDto caso) : ObservableObject
{
    public CasoDto Caso { get; } = caso;

    public ObservableCollection<EvidenciaDto> Evidencias { get; } = [];
    public ObservableCollection<AsientoDto> Asientos { get; } = [];

    [ObservableProperty]
    private bool _cadenaIntegra;

    [ObservableProperty]
    private string? _estadoDeLaCadena;

    [ObservableProperty]
    private string? _error;

    internal ClienteForense Api => api;
    internal INavegacion Navegacion => navegacion;

    [RelayCommand]
    private async Task CargarAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            var evidencias = await api.ListarEvidenciasAsync(Caso.Id, ct);
            var custodia = await api.ExportarCustodiaAsync(Caso.Id, ct);
            Reemplazar(Evidencias, evidencias);
            Reemplazar(Asientos, custodia.Asientos);
            var v = custodia.Verificacion;
            CadenaIntegra = v.EsIntegra;
            EstadoDeLaCadena = v.EsIntegra
                ? $"Cadena íntegra: {v.CantidadAsientos} asientos verificados por el servidor."
                : $"CADENA COMPROMETIDA en el asiento {v.SecuenciaRota}: {v.Motivo}";
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

    [RelayCommand]
    private void Volver() => navegacion.IrACasos();

    private static void Reemplazar<T>(ObservableCollection<T> destino, IEnumerable<T> origen)
    {
        destino.Clear();
        foreach (var item in origen)
            destino.Add(item);
    }
}
