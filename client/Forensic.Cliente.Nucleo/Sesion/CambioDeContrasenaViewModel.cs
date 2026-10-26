using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Sesion;

/// <summary>
/// Cambio de la propia contraseña. Obligatorio con una contraseña inicial o reseteada: el servidor no
/// permite otra cosa hasta cambiarla. La política (largo, etc.) la valida el servidor; acá sólo se
/// confirma que la nueva se escribió dos veces igual.
/// </summary>
public sealed partial class CambioDeContrasenaViewModel(ClienteForense api, INavegacion navegacion, string actual, bool obligatorio)
    : ObservableObject
{
    private string _actual = actual;

    [ObservableProperty]
    private string _nueva = "";

    [ObservableProperty]
    private string _confirmacion = "";

    [ObservableProperty]
    private string? _error;

    public bool Obligatorio { get; } = obligatorio;

    [RelayCommand]
    private async Task CambiarAsync(CancellationToken ct)
    {
        Error = null;
        if (Nueva != Confirmacion)
        {
            Error = "La contraseña nueva y su confirmación no coinciden.";
            return;
        }
        try
        {
            await api.CambiarContrasenaAsync(_actual, Nueva, ct);
            (_actual, Nueva, Confirmacion) = ("", "", "");
            navegacion.IrACasos();
        }
        catch (ErrorDeApi ex)
        {
            Error = Mensajes.Para(ex);
        }
    }
}
