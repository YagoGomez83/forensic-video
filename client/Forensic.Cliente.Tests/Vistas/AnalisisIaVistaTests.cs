using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forensic.Cliente.Api;
using Forensic.Cliente.Controles;
using Forensic.Cliente.Nucleo.Ia;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using Forensic.Cliente.Tests.ViewModels;
using Forensic.Cliente.Vistas;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.Vistas;

public class AnalisisIaVistaTests
{
    private static async Task<(Window Ventana, AnalisisIaViewModel Vm, LienzoDeEvidencia Lienzo)> PantallaAsync()
    {
        var servidor = new ServidorFalso().En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{EvidenciaId}/analisis-ia",
            HttpStatusCode.OK, new[] { AnalisisIaViewModelTests.Analisis() });
        var api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), servidor);
        api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
        var navegacion = new NavegacionRegistrada();
        using var png = new MemoryStream();
        await DecodificacionTests.Recurso("muestra64x48.png").CopyToAsync(png);
        var visor = new VisorViewModel(api, navegacion, null, await VisorViewModelTests.EvidenciaDtoAsync()) { Contenido = png.ToArray() };
        var vm = new AnalisisIaViewModel(
            api, new OrquestadorDeAnalisisIa(api, ClienteWorkerIa.Crear(new Uri("http://127.0.0.1:8100/"), new ServidorFalso())), navegacion, visor);
        var ventana = new Window { Width = 900, Height = 600, Content = new AnalisisIaVista { DataContext = vm } };
        ventana.Show();
        await vm.CargarCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var lienzo = ventana.GetVisualDescendants().OfType<LienzoDeEvidencia>().Single();
        return (ventana, vm, lienzo);
    }

    [AvaloniaFact]
    public async Task La_pantalla_de_ia_avisa_siempre_que_lo_que_produce_es_solo_investigacion()
    {
        var (ventana, _, _) = await PantallaAsync();

        var franja = ventana.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FranjaSoloInvestigacion");

        Assert.True(franja.IsEffectivelyVisible);
        Assert.StartsWith("SÓLO INVESTIGACIÓN", franja.Text);
    }

    [AvaloniaFact]
    public async Task Los_hallazgos_se_dibujan_sobre_la_imagen_decodificada_y_se_listan_como_lo_que_dijo_el_modelo()
    {
        var (ventana, vm, lienzo) = await PantallaAsync();

        Assert.NotNull(lienzo.Imagen);
        Assert.Equal((64, 48), (vm.Visor.AnchoImagen, vm.Visor.AltoImagen));
        Assert.Equal(vm.Hallazgos, lienzo.Hallazgos);
        Assert.Contains(ventana.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "el modelo detectó «car» con 0,82");
    }

    [AvaloniaFact]
    public async Task En_la_pantalla_de_ia_el_boton_izquierdo_no_marca_regiones()
    {
        var (ventana, vm, lienzo) = await PantallaAsync();
        var (a, b) = (lienzo.TranslatePoint(new Point(90, 70), ventana)!.Value, lienzo.TranslatePoint(new Point(200, 160), ventana)!.Value);

        ventana.MouseDown(a, MouseButton.Left);
        ventana.MouseMove(b);
        ventana.MouseUp(b, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.Visor.RegionMarcada);
    }

    [AvaloniaFact]
    public async Task Un_derivado_solo_investigacion_se_marca_en_el_visor()
    {
        var api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), new ServidorFalso());
        var evidencia = await VisorViewModelTests.EvidenciaDtoAsync() with { Via = ViaProcesamiento.SoloInvestigacion };
        var ventana = new Window { Content = new VisorVista { DataContext = new VisorViewModel(api, new NavegacionRegistrada(), null, evidencia) } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        var via = ventana.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Via");

        Assert.Contains("soloInvestigacion", via.Classes);
    }
}
