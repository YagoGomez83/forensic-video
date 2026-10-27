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
    /// <summary>URL del servidor de custodia. No es un secreto: va por variable de entorno.</summary>
    public const string VariableDelServidor = "FORENSIC_SERVIDOR";

    /// <summary>URL del worker de IA de esta workstation. Sólo se acepta loopback.</summary>
    public const string VariableDelWorkerIa = "FORENSIC_WORKER_IA";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime escritorio)
        {
            var servidor = new Uri(Environment.GetEnvironmentVariable(VariableDelServidor) ?? "http://localhost:5065/");
            var api = ClienteForense.Crear(servidor, new SesionEnMemoria());
            var workerIa = ClienteWorkerIa.Crear(
                Environment.GetEnvironmentVariable(VariableDelWorkerIa) is { } url ? new Uri(url) : ClienteWorkerIa.PorDefecto);
            escritorio.MainWindow = new VentanaPrincipal
            {
                DataContext = new VentanaViewModel(api, accion => Dispatcher.UIThread.Post(accion), workerIa),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
