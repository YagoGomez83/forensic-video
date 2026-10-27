using CommunityToolkit.Mvvm.ComponentModel;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Casos;
using Forensic.Cliente.Nucleo.Ia;
using Forensic.Cliente.Nucleo.Sesion;
using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Nucleo;

/// <summary>Navegación entre pantallas. La implementa <see cref="VentanaViewModel"/>.</summary>
public interface INavegacion
{
    void IrALogin(string? aviso = null);
    void IrACambioDeContrasena(string actual, bool obligatorio);
    void IrACasos();
    void IrAAnalisisIa(VisorViewModel visor);
    void IrA(ObservableObject pantalla);
}

/// <summary>
/// La ventana principal: muestra una pantalla por vez. Si el servidor rechaza la sesión (401), vuelve
/// al login desde cualquier pantalla.
/// </summary>
public sealed partial class VentanaViewModel : ObservableObject, INavegacion
{
    private readonly ClienteForense _api;
    private readonly ClienteWorkerIa _workerIa;
    private readonly Action<Action> _enHiloDeUi;

    [ObservableProperty]
    private ObservableObject _actual;

    /// <param name="enHiloDeUi">Cómo ejecutar en el hilo de la UI; por defecto, en el mismo hilo (tests).</param>
    /// <param name="workerIa">El worker de IA de esta workstation; por defecto, el de <see cref="ClienteWorkerIa.PorDefecto"/>.</param>
    public VentanaViewModel(ClienteForense api, Action<Action>? enHiloDeUi = null, ClienteWorkerIa? workerIa = null)
    {
        _api = api;
        _workerIa = workerIa ?? ClienteWorkerIa.Crear(ClienteWorkerIa.PorDefecto);
        _enHiloDeUi = enHiloDeUi ?? (accion => accion());
        _actual = new LoginViewModel(api, this);
        api.Sesion.Expirada += (_, _) =>
            _enHiloDeUi(() => IrALogin("La sesión venció o dejó de ser válida. Volvé a iniciar sesión."));
    }

    public void IrALogin(string? aviso = null) => Actual = new LoginViewModel(_api, this) { Aviso = aviso };

    public void IrACambioDeContrasena(string actual, bool obligatorio) =>
        Actual = new CambioDeContrasenaViewModel(_api, this, actual, obligatorio);

    public void IrACasos() => Actual = new CasosViewModel(_api, this);

    public void IrAAnalisisIa(VisorViewModel visor) =>
        Actual = new AnalisisIaViewModel(_api, new OrquestadorDeAnalisisIa(_api, _workerIa), this, visor);

    public void IrA(ObservableObject pantalla) => Actual = pantalla;
}
