using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Casos;
using Forensic.Cliente.Nucleo.Informes;
using Forensic.Cliente.Tests.Dobles;
using Forensic.Cliente.Tests.ViewModels;
using Forensic.Cliente.Vistas;
using static Forensic.Cliente.Tests.Api.InformesApiTests;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.Vistas;

public class InformesVistaTests
{
    private static async Task<(Window Ventana, InformesViewModel Vm)> PantallaAsync()
    {
        var servidor = new ServidorFalso().En(HttpMethod.Get, $"/casos/{CasoId}/informes", HttpStatusCode.OK,
            new[] { Informe("pericial", 7), Informe("investigativo", 9) });
        var api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), servidor);
        api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
        var navegacion = new NavegacionRegistrada();
        var vm = new InformesViewModel(api, navegacion, new CasoViewModel(api, navegacion, InformesViewModelTests.CasoDto()));
        var ventana = new Window { Width = 1100, Height = 700, Content = new InformesVista { DataContext = vm } };
        ventana.Show();
        await vm.CargarCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        return (ventana, vm);
    }

    private static T Control<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().Single(c => c.Name == nombre);

    [AvaloniaFact]
    public async Task La_franja_de_solo_investigacion_esta_en_el_bloque_investigativo_y_no_en_el_pericial()
    {
        var (ventana, _) = await PantallaAsync();

        var franja = Control<TextBlock>(ventana, "FranjaSoloInvestigacion");
        Assert.True(franja.IsEffectivelyVisible);
        Assert.StartsWith("SÓLO INVESTIGACIÓN — NO ES PRUEBA", franja.Text);
        Assert.Contains(franja, Control<Control>(ventana, "BloqueInvestigativo").GetVisualDescendants());
        Assert.DoesNotContain(
            Control<Control>(ventana, "BloquePericial").GetVisualDescendants().OfType<Border>(),
            b => b.Classes.Contains("soloInvestigacion"));
    }

    [AvaloniaFact]
    public async Task Cada_bloque_lista_sus_informes()
    {
        var (ventana, _) = await PantallaAsync();

        Assert.Equal(1, Control<ListBox>(ventana, "Periciales").ItemCount);
        Assert.Equal(1, Control<ListBox>(ventana, "Investigativos").ItemCount);
    }

    [AvaloniaFact]
    public async Task La_confirmacion_aparece_recien_al_pedir_la_emision_y_solo_en_su_bloque()
    {
        var (ventana, vm) = await PantallaAsync();
        var confirmarPericial = Control<Control>(ventana, "ConfirmacionPericial");
        var confirmarInvestigativo = Control<Control>(ventana, "ConfirmacionInvestigativa");
        Assert.False(confirmarPericial.IsVisible);
        Assert.False(confirmarInvestigativo.IsVisible);

        Control<Button>(ventana, "EmitirPericial").Command!.Execute(Control<Button>(ventana, "EmitirPericial").CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(TipoDeInforme.Pericial, vm.EmisionPendiente);
        Assert.True(confirmarPericial.IsVisible);
        Assert.False(confirmarInvestigativo.IsVisible);
    }
}
