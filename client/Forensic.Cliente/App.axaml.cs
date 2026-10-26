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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime escritorio)
        {
            var servidor = new Uri(Environment.GetEnvironmentVariable(VariableDelServidor) ?? "http://localhost:5065/");
            var api = ClienteForense.Crear(servidor, new SesionEnMemoria());
            escritorio.MainWindow = new VentanaPrincipal
            {
                DataContext = new VentanaViewModel(api, accion => Dispatcher.UIThread.Post(accion)),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
