using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forensic.Cliente.Api;
using Forensic.Cliente.Controles;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using Forensic.Cliente.Tests.ViewModels;
using Forensic.Cliente.Vistas;

namespace Forensic.Cliente.Tests.Vistas;

public class VisorVistaTests
{
    internal static async Task<(Window Ventana, VisorViewModel Vm, LienzoDeEvidencia Lienzo)> VisorConImagenAsync()
    {
        var api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), new ServidorFalso());
        var vm = new VisorViewModel(api, new NavegacionRegistrada(), null, await VisorViewModelTests.EvidenciaDtoAsync());
        using var png = new MemoryStream();
        await DecodificacionTests.Recurso("muestra64x48.png").CopyToAsync(png);
        vm.Contenido = png.ToArray();
        var ventana = new Window { Width = 800, Height = 600, Content = new VisorVista { DataContext = vm } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        var lienzo = ventana.GetVisualDescendants().OfType<LienzoDeEvidencia>().Single();
        return (ventana, vm, lienzo);
    }

    [AvaloniaFact]
    public async Task El_visor_decodifica_la_imagen_y_la_ajusta_a_la_vista()
    {
        var (_, vm, lienzo) = await VisorConImagenAsync();

        Assert.Equal((64, 48), (vm.AnchoImagen, vm.AltoImagen));
        Assert.NotNull(lienzo.Imagen);
        Assert.True(vm.Transformacion.Zoom > 1);
        Assert.Same(vm.Transformacion, lienzo.Transformacion);
    }

    [AvaloniaFact]
    public async Task La_rueda_hace_zoom_dejando_quieto_el_pixel_bajo_el_cursor()
    {
        var (ventana, vm, lienzo) = await VisorConImagenAsync();
        var enLienzo = new Point(lienzo.Bounds.Width / 3, lienzo.Bounds.Height / 2);
        var enVentana = lienzo.TranslatePoint(enLienzo, ventana)!.Value;
        var cursor = new Punto(enLienzo.X, enLienzo.Y);
        var (zoomAntes, pixelAntes) = (vm.Transformacion.Zoom, vm.Transformacion.PantallaAImagen(cursor));

        ventana.MouseWheel(enVentana, new Vector(0, 1));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(zoomAntes * LienzoDeEvidencia.FactorDeZoom, vm.Transformacion.Zoom, 9);
        var pixelDespues = vm.Transformacion.PantallaAImagen(cursor);
        Assert.Equal(pixelAntes.X, pixelDespues.X, 9);
        Assert.Equal(pixelAntes.Y, pixelDespues.Y, 9);
    }

    [AvaloniaFact]
    public async Task Arrastrar_con_el_boton_derecho_desplaza_la_imagen()
    {
        var (ventana, vm, lienzo) = await VisorConImagenAsync();
        var desde = lienzo.TranslatePoint(new Point(100, 100), ventana)!.Value;
        var antes = vm.Transformacion.Desplazamiento;

        ventana.MouseDown(desde, MouseButton.Right);
        ventana.MouseMove(desde + new Point(30, -10));
        ventana.MouseUp(desde + new Point(30, -10), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(antes.X + 30, vm.Transformacion.Desplazamiento.X, 9);
        Assert.Equal(antes.Y - 10, vm.Transformacion.Desplazamiento.Y, 9);
    }
}
