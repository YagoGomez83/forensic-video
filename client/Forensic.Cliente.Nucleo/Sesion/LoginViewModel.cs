using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Sesion;

public sealed partial class LoginViewModel(ClienteForense api, INavegacion navegacion) : ObservableObject
{
    [ObservableProperty]
    private string _usuario = "";

    [ObservableProperty]
    private string _contrasena = "";

    /// <summary>Por qué se volvió al login (p. ej. la sesión venció).</summary>
    [ObservableProperty]
    private string? _aviso;

    [ObservableProperty]
    private string? _error;

    public string Servidor => api.Servidor.ToString();

    [RelayCommand]
    private async Task IniciarAsync(CancellationToken ct)
    {
        Error = null;
        var contrasena = Contrasena;
        // La contraseña no queda en la pantalla más de lo necesario.
        Contrasena = "";
        try
        {
            var estado = await api.IniciarSesionAsync(Usuario.Trim(), contrasena, ct);
            if (estado == EstadoDeInicio.DebeCambiarContrasena)
                navegacion.IrACambioDeContrasena(contrasena, obligatorio: true);
            else
                navegacion.IrACasos();
        }
        catch (ErrorDeApi ex)
        {
            Error = Mensajes.Para(ex);
        }
    }
}
