using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo;
using Forensic.Cliente.Tests.Dobles;
using Forensic.Cliente.Vistas;

namespace Forensic.Cliente.Tests.Vistas;

/// <summary>
/// Lo que ve el analista al abrir el programa. Instalado, no hay consola: si la configuración no sirve,
/// el motivo tiene que estar en pantalla, no en una excepción que cierra la app sin decir nada.
/// </summary>
public class ArranqueTests
{
    private static string Textos(Window ventana) =>
        string.Join("\n", ventana.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    [AvaloniaFact]
    public void Con_la_configuracion_bien_abre_la_ventana_principal()
    {
        var ventana = App.ArmarVentana(() =>
            new VentanaViewModel(ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), new ServidorFalso())));

        Assert.IsType<VentanaPrincipal>(ventana);
        Assert.Equal($"Forensic Video {App.Version}", ventana.Title);
    }

    [Fact]
    public void La_version_dice_el_commit_del_que_salio()
    {
        // En desarrollo, «0.0.0-desarrollo+<commit>»; en un release, «v1.2.3-<12 del commit>».
        Assert.Matches(@"^(v\d+\.\d+\.\d+-[0-9a-f]{12}|0\.0\.0-desarrollo(\+[0-9a-f]{40})?)$", App.Version);
    }

    [AvaloniaFact]
    public void Si_la_configuracion_no_sirve_muestra_el_motivo_y_donde_corregirlo()
    {
        var ventana = App.ArmarVentana(() =>
            throw new ConfiguracionInvalidaException("Con https hace falta la CA del laboratorio: definí «caServidor»."));
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<VentanaDeErrorDeArranque>(ventana);
        Assert.Equal($"Forensic Video {App.Version}", ventana.Title);
        var textos = Textos(ventana);
        Assert.Contains("Con https hace falta la CA del laboratorio: definí «caServidor».", textos);
        Assert.Contains(ConfiguracionDeWorkstation.RutaPorDefecto, textos);
    }

    [AvaloniaFact]
    public void Un_certificado_que_no_carga_tambien_se_muestra()
    {
        var ventana = App.ArmarVentana(() =>
            throw new InvalidOperationException("No está en el almacén personal un certificado con clave y huella 0123."));
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("huella 0123", Textos(ventana));
    }
}
