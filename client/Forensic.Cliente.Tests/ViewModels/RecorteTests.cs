using System.Net;
using System.Text.Json;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.ViewModels;

public class RecorteTests
{
    internal static readonly Guid DerivadoId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    internal static object Aplicada(string tipo = "opencv.recorte/v1", string parametros = """{"alto":9,"ancho":16,"x":5,"y":6}""") => new
    {
        derivado = Evidencia(DerivadoId, padre: EvidenciaId, nombre: "cam1-recorte.png"),
        operacionId = Guid.NewGuid(),
        tipo,
        parametros,
        entorno = """{"opencv":"4.14.0"}""",
    };

    private readonly ServidorFalso _servidor = new();
    private readonly NavegacionRegistrada _navegacion = new();
    private readonly ClienteForense _api;

    public RecorteTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    private async Task<VisorViewModel> VisorAsync()
    {
        var vm = new VisorViewModel(_api, _navegacion, null, await VisorViewModelTests.EvidenciaDtoAsync());
        vm.ImagenDecodificada(64, 48);
        vm.Transformacion = new TransformacionDeVista(2.0, new Punto(100, 50));
        return vm;
    }

    [Fact]
    public async Task Marcar_un_arrastre_en_pantalla_da_la_region_en_pixeles_de_la_imagen()
    {
        var vm = await VisorAsync();

        vm.MarcarRegion(new Punto(111, 63), new Punto(140.5, 80));

        Assert.Equal(new Region(5, 6, 16, 9), vm.RegionMarcada);
    }

    [Fact]
    public async Task Sin_region_marcada_no_se_puede_aplicar_el_recorte()
    {
        var vm = await VisorAsync();

        Assert.False(vm.AplicarRecorteCommand.CanExecute(null));
        vm.MarcarRegion(new Punto(111, 63), new Punto(140.5, 80));
        Assert.True(vm.AplicarRecorteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Aplicar_envia_la_region_en_pixeles_de_la_imagen_y_abre_el_derivado_del_servidor()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{EvidenciaId}/operaciones", HttpStatusCode.Created, Aplicada());
        var vm = await VisorAsync();
        vm.MarcarRegion(new Punto(111, 63), new Punto(140.5, 80));

        await vm.AplicarRecorteCommand.ExecuteAsync(null);

        var pedido = JsonDocument.Parse(_servidor.Cuerpos[^1]!).RootElement;
        Assert.Equal("opencv.recorte/v1", pedido.GetProperty("tipo").GetString());
        var p = pedido.GetProperty("parametros");
        Assert.Equal((5, 6, 16, 9), (p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32(), p.GetProperty("ancho").GetInt32(), p.GetProperty("alto").GetInt32()));

        var derivado = Assert.IsType<VisorViewModel>(_navegacion.Actual);
        Assert.Equal(DerivadoId, derivado.Evidencia.Id);
        // Lo que se muestra es lo que quedó asentado en el servidor, no lo que se pidió.
        Assert.Equal("opencv.recorte/v1 {\"alto\":9,\"ancho\":16,\"x\":5,\"y\":6}", derivado.OperacionDeOrigen);
    }

    [Fact]
    public async Task Si_el_servidor_rechaza_el_recorte_muestra_su_motivo_y_sigue_en_la_evidencia()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{EvidenciaId}/operaciones",
            _ => ServidorFalso.Problema(HttpStatusCode.UnprocessableEntity, "Operación rechazada", "La región se sale de la imagen."));
        var vm = await VisorAsync();
        vm.MarcarRegion(new Punto(111, 63), new Punto(140.5, 80));

        await vm.AplicarRecorteCommand.ExecuteAsync(null);

        Assert.Equal("La región se sale de la imagen.", vm.Error);
        Assert.Null(_navegacion.Actual);
    }

    [Fact]
    public async Task Una_divergencia_del_worker_se_muestra_como_alerta_de_seguridad()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{EvidenciaId}/operaciones",
            _ => ServidorFalso.Problema(HttpStatusCode.BadGateway, "Divergencia", "El worker reportó otro hash para la salida."));
        var vm = await VisorAsync();
        vm.MarcarRegion(new Punto(111, 63), new Punto(140.5, 80));

        await vm.AplicarRecorteCommand.ExecuteAsync(null);

        Assert.StartsWith("ALERTA DE SEGURIDAD", vm.Error);
        Assert.Contains("El worker reportó otro hash para la salida.", vm.Error);
    }
}
