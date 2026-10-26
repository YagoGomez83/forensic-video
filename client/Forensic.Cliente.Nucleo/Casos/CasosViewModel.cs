using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Casos;

/// <summary>Los casos que el servidor deja ver al analista, y la apertura de uno nuevo.</summary>
public sealed partial class CasosViewModel(ClienteForense api, INavegacion navegacion) : ObservableObject
{
    public ObservableCollection<CasoDto> Casos { get; } = [];

    [ObservableProperty]
    private string _nuevoNumero = "";

    [ObservableProperty]
    private string _nuevaDescripcion = "";

    [ObservableProperty]
    private string? _error;

    [RelayCommand]
    private async Task CargarAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            var casos = await api.ListarCasosAsync(ct);
            Casos.Clear();
            foreach (var caso in casos)
                Casos.Add(caso);
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
    private async Task AbrirNuevoAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            var caso = await api.AbrirCasoAsync(NuevoNumero.Trim(), NuevaDescripcion, ct);
            (NuevoNumero, NuevaDescripcion) = ("", "");
            Abrir(caso);
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
    private void Abrir(CasoDto caso) => navegacion.IrA(new CasoViewModel(api, navegacion, caso));
}
