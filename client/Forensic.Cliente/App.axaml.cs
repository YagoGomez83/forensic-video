using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo;
using Forensic.Cliente.Vistas;

namespace Forensic.Cliente;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime escritorio)
        {
            var config = ConfiguracionDeWorkstation.Cargar(ConfiguracionDeWorkstation.RutaPorDefecto, Environment.GetEnvironmentVariable);
            // https exige TLS mutuo (CA y certificado de esta workstation); en claro, sólo loopback.
            var api = ClienteForense.Crear(config.Servidor, new SesionEnMemoria(), CanalSeguro.Para(config));
            var workerIa = ClienteWorkerIa.Crear(config.WorkerIa);
            escritorio.MainWindow = new VentanaPrincipal
            {
                DataContext = new VentanaViewModel(api, accion => Dispatcher.UIThread.Post(accion), workerIa),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
