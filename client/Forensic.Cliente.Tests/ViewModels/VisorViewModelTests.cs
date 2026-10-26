using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Casos;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.ViewModels;

public class VisorViewModelTests
{
    private readonly ServidorFalso _servidor = new();
    private readonly NavegacionRegistrada _navegacion = new();
    private readonly ClienteForense _api;

    public VisorViewModelTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    internal static async Task<EvidenciaDto> EvidenciaDtoAsync(long tamano = 2048, Guid? id = null, Guid? padre = null, string nombre = "cam1.png")
    {
        var e = new
        {
            id = id ?? EvidenciaId, casoId = CasoId, hash = new string('a', 64), tamanoBytes = tamano, nombreOriginal = nombre,
            via = "AdmisibleEnJuicio", padreId = padre, ingestadaEn = DateTimeOffset.UtcNow, ingestadaPor = "lucia", controlCruzadoPendiente = false,
        };
        var servidor = new ServidorFalso().En(HttpMethod.Get, $"/casos/{CasoId}/evidencias", HttpStatusCode.OK, new[] { e });
        return (await ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), servidor).ListarEvidenciasAsync(CasoId, CancellationToken.None))[0];
    }

    private VisorViewModel Visor(EvidenciaDto evidencia) => new(_api, _navegacion, volverA: null, evidencia);

    [Fact]
    public async Task Cargar_trae_el_contenido_verificado_del_servidor_a_memoria()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{EvidenciaId}/contenido",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var visor = Visor(await EvidenciaDtoAsync());

        await visor.CargarCommand.ExecuteAsync(null);

        Assert.Equal(bytes, visor.Contenido);
        Assert.Null(visor.SinPrevisualizacion);
    }

    [Fact]
    public async Task Una_evidencia_demasiado_grande_no_se_descarga_para_previsualizar()
    {
        var visor = Visor(await EvidenciaDtoAsync(tamano: VisorViewModel.TamanoMaximoDePrevisualizacion + 1));

        await visor.CargarCommand.ExecuteAsync(null);

        Assert.Empty(_servidor.Peticiones);
        Assert.Null(visor.Contenido);
        Assert.NotNull(visor.SinPrevisualizacion);
    }

    [Fact]
    public async Task Si_el_servidor_detecta_la_integridad_comprometida_lo_muestra()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{EvidenciaId}/contenido",
            _ => ServidorFalso.Problema(HttpStatusCode.Locked, "Integridad comprometida", "Integridad comprometida de la evidencia: el hash no coincide."));
        var visor = Visor(await EvidenciaDtoAsync());

        await visor.CargarCommand.ExecuteAsync(null);

        Assert.Equal("Integridad comprometida de la evidencia: el hash no coincide.", visor.Error);
        Assert.Null(visor.Contenido);
    }

    [Fact]
    public async Task Al_decodificar_se_ajusta_la_imagen_a_la_vista_y_el_zoom_sigue_al_cursor()
    {
        var visor = Visor(await EvidenciaDtoAsync());

        visor.ImagenDecodificada(64, 48);
        visor.AjustarA(640, 480);
        var cursor = new Punto(100, 100);
        var antes = visor.Transformacion.PantallaAImagen(cursor);
        visor.ZoomEn(2, cursor);

        Assert.Equal((64, 48), (visor.AnchoImagen, visor.AltoImagen));
        Assert.Equal(20.0, visor.Transformacion.Zoom, 9);
        Assert.Equal(antes, visor.Transformacion.PantallaAImagen(cursor));
    }

    [Fact]
    public async Task Un_formato_que_no_se_puede_decodificar_lo_dice_sin_inventar_una_imagen()
    {
        var visor = Visor(await EvidenciaDtoAsync(nombre: "cam1.mkv"));

        visor.NoDecodificable();

        Assert.Contains("no se puede previsualizar", visor.SinPrevisualizacion);
        Assert.Equal((0, 0), (visor.AnchoImagen, visor.AltoImagen));
    }
}
