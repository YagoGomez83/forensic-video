using CommunityToolkit.Mvvm.ComponentModel;
using Forensic.Cliente.Nucleo;

namespace Forensic.Cliente.Tests.Dobles;

/// <summary>Navegación de prueba: registra a qué pantalla se fue.</summary>
internal sealed class NavegacionRegistrada : INavegacion
{
    public ObservableObject? Actual { get; private set; }
    public string? AvisoDeLogin { get; private set; }
    public bool FueALogin { get; private set; }
    public bool FueACasos { get; private set; }

    public void IrALogin(string? aviso = null) => (FueALogin, AvisoDeLogin) = (true, aviso);

    public void IrACambioDeContrasena(string actual, bool obligatorio)
    {
    }

    public void IrACasos() => FueACasos = true;

    public void IrA(ObservableObject pantalla) => Actual = pantalla;
}
