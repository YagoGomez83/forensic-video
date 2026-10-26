using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;
using static Forensic.Cliente.Tests.ViewModels.RecorteTests;

namespace Forensic.Cliente.Tests.ViewModels;

public class LinajeTests
{
    private static object Linaje(params object[] reproducciones) => new object[]
    {
        new { evidencia = Evidencia(), operacion = (object?)null, reproducciones = Array.Empty<object>() },
        new
        {
            evidencia = Evidencia(DerivadoId, padre: EvidenciaId, nombre: "cam1-recorte.png"),
            operacion = new
            {
                secuencia = 3, tipo = "opencv.recorte/v1", parametros = """{"alto":9,"ancho":16,"x":5,"y":6}""", evidenciaEntradaId = EvidenciaId,
                entorno = """{"opencv":"4.14.0"}""", via = "AdmisibleEnJuicio", actor = "lucia", timestamp = DateTimeOffset.UtcNow,
            },
            reproducciones,
        },
    };

    private static object Resultado(bool reproducido, bool entornoCoincide = true) => new
    {
        evidenciaId = DerivadoId, reproducido, entornoCoincide,
        sha256Esperado = new string('e', 64), sha256Obtenido = reproducido ? new string('e', 64) : new string('f', 64),
        sha256PixelesEsperado = new string('p', 64), sha256PixelesObtenido = new string('p', 64),
        entornoAsentado = """{"opencv":"4.14.0"}""", entornoActual = entornoCoincide ? """{"opencv":"4.14.0"}""" : """{"opencv":"4.15.0"}""",
    };

    private readonly ServidorFalso _servidor = new();
    private readonly ClienteForense _api;

    public LinajeTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    private async Task<LinajeViewModel> LinajeAsync() =>
        new(_api, new NavegacionRegistrada(), null, await VisorViewModelTests.EvidenciaDtoAsync(id: DerivadoId, padre: EvidenciaId, nombre: "cam1-recorte.png"));

    [Fact]
    public async Task El_linaje_muestra_del_original_al_derivado_con_cada_operacion_asentada()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{DerivadoId}/linaje", HttpStatusCode.OK, Linaje());
        var vm = await LinajeAsync();

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(["cam1.png", "cam1-recorte.png"], vm.Eslabones.Select(e => e.Evidencia.NombreOriginal));
        Assert.Null(vm.Eslabones[0].Operacion);
        Assert.Equal(("opencv.recorte/v1", """{"alto":9,"ancho":16,"x":5,"y":6}"""), (vm.Eslabones[1].Operacion!.Tipo, vm.Eslabones[1].Operacion!.Parametros));
        Assert.Equal("""{"opencv":"4.14.0"}""", vm.Eslabones[1].Operacion!.Entorno);
    }

    [Fact]
    public async Task Reproducir_muestra_el_veredicto_del_servidor_y_recarga_el_linaje_con_la_reproduccion_asentada()
    {
        var reproducida = false;
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{DerivadoId}/reproducir", _ =>
            {
                reproducida = true;
                return ServidorFalso.Respuesta(HttpStatusCode.OK, Resultado(reproducido: true));
            })
            .En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{DerivadoId}/linaje", _ => ServidorFalso.Respuesta(HttpStatusCode.OK,
                reproducida ? Linaje(new { secuencia = 4, reproducido = true, entornoCoincide = true, actor = "lucia", timestamp = DateTimeOffset.UtcNow }) : Linaje()));
        var vm = await LinajeAsync();
        await vm.CargarCommand.ExecuteAsync(null);

        await vm.ReproducirCommand.ExecuteAsync(null);

        Assert.True(vm.Reproducido);
        Assert.Equal($"Se reprodujo byte a byte: SHA-256 {new string('e', 64)}. Entorno igual al asentado.", vm.Veredicto);
        Assert.True(Assert.Single(vm.Eslabones[1].Reproducciones).Reproducido);
    }

    [Fact]
    public async Task Si_no_reproduce_lo_dice_con_los_dos_hashes_y_el_entorno()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{DerivadoId}/reproducir", HttpStatusCode.OK, Resultado(reproducido: false, entornoCoincide: false))
            .En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{DerivadoId}/linaje", HttpStatusCode.OK, Linaje());
        var vm = await LinajeAsync();

        await vm.ReproducirCommand.ExecuteAsync(null);

        Assert.False(vm.Reproducido);
        Assert.Equal(
            $"NO se reprodujo: se esperaba {new string('e', 64)} y se obtuvo {new string('f', 64)}. El entorno del worker es distinto del asentado.",
            vm.Veredicto);
    }

    [Fact]
    public async Task Con_la_cadena_comprometida_muestra_el_rechazo_del_servidor()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias/{DerivadoId}/reproducir",
            _ => ServidorFalso.Problema(HttpStatusCode.Locked, "Cadena comprometida", "La cadena del caso no verifica; no se reproduce."));
        var vm = await LinajeAsync();

        await vm.ReproducirCommand.ExecuteAsync(null);

        Assert.Equal("La cadena del caso no verifica; no se reproduce.", vm.Error);
        Assert.Null(vm.Veredicto);
    }

    [Fact]
    public async Task Un_original_no_ofrece_reproducir()
    {
        var vm = new LinajeViewModel(_api, new NavegacionRegistrada(), null, await VisorViewModelTests.EvidenciaDtoAsync());

        Assert.False(vm.ReproducirCommand.CanExecute(null));
    }
}
