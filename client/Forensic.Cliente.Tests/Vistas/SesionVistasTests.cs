using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo;
using Forensic.Cliente.Tests.Dobles;
using Forensic.Cliente.Vistas;

namespace Forensic.Cliente.Tests.Vistas;

public class SesionVistasTests
{
    private static T Buscar<T>(Control raiz, string nombre) where T : Control =>
        raiz.GetVisualDescendants().OfType<T>().Single(c => c.Name == nombre);

    [AvaloniaFact]
    public async Task Lo_que_se_escribe_en_el_login_llega_al_view_model_y_entrar_navega_a_los_casos()
    {
        var servidor = new ServidorFalso()
            .En(HttpMethod.Post, "/auth/login", HttpStatusCode.OK, new { token = "t", expiraEn = DateTimeOffset.UtcNow.AddMinutes(15) })
            .En(HttpMethod.Get, "/casos", HttpStatusCode.OK, Array.Empty<object>());
        var ventana = new VentanaPrincipal
        {
            DataContext = new VentanaViewModel(ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), servidor)),
        };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Buscar<TextBox>(ventana, "Usuario").Text = "lucia";
        Buscar<TextBox>(ventana, "Contrasena").Text = "su contrasena";
        var entrar = Buscar<Button>(ventana, "Entrar");
        entrar.Command!.Execute(null);
        for (var i = 0; i < 50 && !ventana.GetVisualDescendants().OfType<CasosVista>().Any(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Single(ventana.GetVisualDescendants().OfType<CasosVista>());
        Assert.Contains("\"usuario\":\"lucia\"", servidor.Cuerpos[0]);
        Assert.Contains("\"contrasena\":\"su contrasena\"", servidor.Cuerpos[0]);
    }
}
