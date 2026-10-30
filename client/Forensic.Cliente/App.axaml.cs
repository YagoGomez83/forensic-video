using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo;
using Forensic.Cliente.Vistas;

namespace Forensic.Cliente;

public sealed class App : Application
{
    /// <summary>
    /// Qué versión corre esta workstation: en un release, el tag y el commit («v1.2.3-0123456789ab»); en
    /// desarrollo, «0.0.0-desarrollo» y el commit que agrega el SDK.
    /// </summary>
    public static string Version { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime escritorio)
        {
            escritorio.MainWindow = ArmarVentana(() =>
            {
                var config = ConfiguracionDeWorkstation.Cargar(ConfiguracionDeWorkstation.RutaPorDefecto, Environment.GetEnvironmentVariable);
                // https exige TLS mutuo (CA y certificado de esta workstation); en claro, sólo loopback.
                var api = ClienteForense.Crear(config.Servidor, new SesionEnMemoria(), CanalSeguro.Para(config));
                return new VentanaViewModel(api, accion => Dispatcher.UIThread.Post(accion), ClienteWorkerIa.Crear(config.WorkerIa));
            });
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// La ventana principal o, si no se puede armar (configuración inválida, CA o certificado que no
    /// cargan), la que dice por qué: instalado no hay consola, y una excepción cerraría la app sin aviso.
    /// </summary>
    internal static Window ArmarVentana(Func<VentanaViewModel> armar)
    {
        Window ventana;
        try
        {
            ventana = new VentanaPrincipal { DataContext = armar() };
        }
        catch (Exception ex)
        {
            ventana = new VentanaDeErrorDeArranque
            {
                DataContext = new ErrorDeArranqueViewModel(ex.Message, ConfiguracionDeWorkstation.RutaPorDefecto),
            };
        }
        ventana.Title = $"Forensic Video {Version}";
        return ventana;
    }
}
