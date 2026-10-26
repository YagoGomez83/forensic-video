using System.Net;
using System.Text.Json;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Operaciones;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;
using static Forensic.Cliente.Tests.ViewModels.RecorteTests;

namespace Forensic.Cliente.Tests.ViewModels;

/// <summary>
/// Cada formulario arma los parámetros en las unidades del servidor. El cliente no valida rangos (eso
/// es del servidor); sólo que el dato esté y que la conversión de unidades sea exacta.
/// </summary>
public class FormulariosTests
{
    internal static string Json(object parametros) => JsonSerializer.Serialize(parametros, ClienteForense.Json);

    private static readonly MarcasDelVisor SinMarcas = new(null, []);

    [Theory]
    [InlineData(12.5, 2, 1250)]
    [InlineData(-0.07, 2, -7)]
    [InlineData(2.2, 3, 2200)]
    [InlineData(3, 1, 30)]
    public void Punto_fijo_convierte_exacto(double valor, int decimales, int esperado)
    {
        Assert.Equal(esperado, PuntoFijo.AEntero((decimal)valor, decimales, "campo"));
    }

    [Fact]
    public void Punto_fijo_no_redondea_en_silencio_ni_acepta_un_dato_vacio()
    {
        Assert.Throws<ErrorDeFormulario>(() => PuntoFijo.AEntero(12.555m, 2, "ángulo"));
        Assert.Throws<ErrorDeFormulario>(() => PuntoFijo.AEntero(null, 2, "ángulo"));
    }

    [Fact]
    public void La_perspectiva_usa_los_cuatro_puntos_marcados_en_pixeles_de_la_imagen()
    {
        var f = new FormularioDePerspectiva { Ancho = 40, Alto = 30, Interpolacion = "cubica" };
        var puntos = new PuntoDeImagen[] { new(3, 2), new(60, 4), new(62, 45), new(1, 44) };

        var p = Json(f.Parametros(new MarcasDelVisor(null, puntos)));

        Assert.Equal("""{"x1":3,"y1":2,"x2":60,"y2":4,"x3":62,"y3":45,"x4":1,"y4":44,"ancho":40,"alto":30,"interpolacion":"cubica"}""", p);
        Assert.Equal(ModoDeMarcado.Puntos, f.Marcado);
    }

    [Fact]
    public void La_perspectiva_sin_los_cuatro_puntos_no_se_arma()
    {
        var f = new FormularioDePerspectiva { Ancho = 40, Alto = 30 };

        var ex = Assert.Throws<ErrorDeFormulario>(() => f.Parametros(new MarcasDelVisor(null, [new(1, 1), new(2, 2)])));
        Assert.Contains("4 puntos", ex.Message);
    }

    [Fact]
    public async Task Marcar_puntos_en_el_visor_los_traduce_a_pixeles_de_la_imagen_y_el_quinto_reinicia()
    {
        var vm = await VisorConFormularioAsync<FormularioDePerspectiva>();
        vm.Transformacion = new TransformacionDeVista(4.0, new Punto(10, 20));

        foreach (var (x, y) in new[] { (22.0, 29.0), (250.0, 30.0), (258.0, 200.0), (14.0, 196.0) })
            vm.AgregarPunto(new Punto(x, y));

        Assert.Equal([new(3, 2), new(60, 2), new(62, 45), new(1, 44)], vm.PuntosMarcados);
        vm.AgregarPunto(new Punto(18, 24));
        Assert.Equal([new PuntoDeImagen(2, 1)], vm.PuntosMarcados);
    }

    [Fact]
    public async Task Aplicar_la_operacion_elegida_envia_su_tipo_y_sus_parametros()
    {
        var (vm, servidor) = await VisorConServidorAsync<FormularioDePerspectiva>(Aplicada("opencv.perspectiva/v1", "{}"));
        vm.Transformacion = new TransformacionDeVista(1.0, new Punto(0, 0));
        foreach (var (x, y) in new[] { (3.5, 2.5), (60.5, 4.5), (62.5, 45.5), (1.5, 44.5) })
            vm.AgregarPunto(new Punto(x, y));
        var f = Assert.IsType<FormularioDePerspectiva>(vm.FormularioElegido);
        (f.Ancho, f.Alto) = (40, 30);

        await vm.AplicarOperacionCommand.ExecuteAsync(null);

        var pedido = JsonDocument.Parse(servidor.Cuerpos[^1]!).RootElement;
        Assert.Equal("opencv.perspectiva/v1", pedido.GetProperty("tipo").GetString());
        Assert.Equal(3, pedido.GetProperty("parametros").GetProperty("x1").GetInt32());
        Assert.Equal(40, pedido.GetProperty("parametros").GetProperty("ancho").GetInt32());
    }

    [Fact]
    public async Task Un_formulario_incompleto_muestra_que_falta_sin_llamar_al_servidor()
    {
        var (vm, servidor) = await VisorConServidorAsync<FormularioDePerspectiva>(Aplicada());

        await vm.AplicarOperacionCommand.ExecuteAsync(null);

        Assert.Contains("4 puntos", vm.Error);
        Assert.DoesNotContain(servidor.Peticiones, p => p.Method == HttpMethod.Post);
    }

    internal static async Task<VisorViewModel> VisorConFormularioAsync<T>() where T : FormularioDeOperacion =>
        (await VisorConServidorAsync<T>(Aplicada())).Vm;

    internal static async Task<(VisorViewModel Vm, ServidorFalso Servidor)> VisorConServidorAsync<T>(object respuesta) where T : FormularioDeOperacion
    {
        var servidor = new ServidorFalso().En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{EvidenciaId}/operaciones", HttpStatusCode.Created, respuesta);
        var api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), servidor);
        api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
        var vm = new VisorViewModel(api, new NavegacionRegistrada(), null, await VisorViewModelTests.EvidenciaDtoAsync());
        vm.ImagenDecodificada(64, 48);
        vm.FormularioElegido = vm.Formularios.OfType<T>().Single();
        return (vm, servidor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void La_rotacion_exacta_envia_los_cuartos_de_vuelta(int giros)
    {
        Assert.Equal($"{{\"giros\":{giros}}}", Json(new FormularioDeRotacion90 { Giros = giros }.Parametros(SinMarcas)));
    }

    [Fact]
    public void La_rotacion_por_angulo_convierte_los_grados_a_centesimas_exactas()
    {
        var f = new FormularioDeRotacion { Grados = -12.5m, Interpolacion = "cubica" };

        Assert.Equal("""{"centesimasDeGrado":-1250,"interpolacion":"cubica"}""", Json(f.Parametros(SinMarcas)));
    }

    [Fact]
    public void La_rotacion_por_angulo_no_redondea_mas_de_dos_decimales()
    {
        var ex = Assert.Throws<ErrorDeFormulario>(() => new FormularioDeRotacion { Grados = 12.555m }.Parametros(SinMarcas));

        Assert.Contains("2 decimales", ex.Message);
    }
}
