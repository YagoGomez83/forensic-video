using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Casos;
using Forensic.Cliente.Nucleo.Informes;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.Api.InformesApiTests;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.ViewModels;

public class InformesViewModelTests
{
    private readonly ServidorFalso _servidor = new();
    private readonly NavegacionRegistrada _navegacion = new();
    private readonly ClienteForense _api;
    private readonly CasoViewModel _caso;

    public InformesViewModelTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
        _caso = new CasoViewModel(_api, _navegacion, CasoDto());
    }

    internal static CasoDto CasoDto() =>
        new(CasoId, "EXP-1", "Robo", EstadoCaso.Abierto, DateTimeOffset.UtcNow, "lucia", ["lucia"]);

    private InformesViewModel Vm() => new(_api, _navegacion, _caso);

    private int Emisiones => _servidor.Peticiones.Count(p => p.Method == HttpMethod.Post);

    [Fact]
    public async Task Carga_los_informes_separados_por_tipo()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/informes", HttpStatusCode.OK,
            new[] { Informe("pericial", 7), Informe("investigativo", 9), Informe("pericial", 12) });
        var vm = Vm();

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal([7L, 12L], vm.Periciales.Select(i => i.Secuencia));
        Assert.Equal([9L], vm.Investigativos.Select(i => i.Secuencia));
    }

    [Theory]
    [InlineData(TipoDeInforme.Pericial)]
    [InlineData(TipoDeInforme.Investigativo)]
    public void Preparar_la_emision_no_llama_al_servidor_y_cancelarla_tampoco(TipoDeInforme tipo)
    {
        var vm = Vm();

        vm.PrepararEmisionCommand.Execute(tipo);
        Assert.Equal(tipo, vm.EmisionPendiente);
        Assert.Equal(tipo == TipoDeInforme.Pericial, vm.ConfirmandoPericial);
        Assert.Equal(tipo == TipoDeInforme.Investigativo, vm.ConfirmandoInvestigativo);
        vm.CancelarEmisionCommand.Execute(null);

        Assert.Null(vm.EmisionPendiente);
        Assert.False(vm.ConfirmarEmisionCommand.CanExecute(null));
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task Confirmar_emite_el_pericial_por_su_ruta_recarga_la_lista_y_dice_que_quedo_asentado()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/informes/pericial", HttpStatusCode.Created, Informe("pericial", 7, excluidas: 1, opacos: 2))
            .En(HttpMethod.Get, $"/casos/{CasoId}/informes", HttpStatusCode.OK, new[] { Informe("pericial", 7, excluidas: 1, opacos: 2) });
        var vm = Vm();

        vm.PrepararEmisionCommand.Execute(TipoDeInforme.Pericial);
        await vm.ConfirmarEmisionCommand.ExecuteAsync(null);

        Assert.Equal(1, Emisiones);
        Assert.Null(vm.EmisionPendiente);
        Assert.Equal([7L], vm.Periciales.Select(i => i.Secuencia));
        Assert.Equal(
            "Informe pericial emitido y asentado en el asiento 7: cubre la cadena hasta el asiento 6, con 2 evidencias; " +
            "1 evidencia excluida por ser «sólo investigación» y 2 asientos opacos.",
            vm.Aviso);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task El_investigativo_se_emite_por_su_ruta_y_el_aviso_dice_que_es_solo_investigacion()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/informes/investigativo", HttpStatusCode.Created, Informe("investigativo", 9))
            .En(HttpMethod.Get, $"/casos/{CasoId}/informes", HttpStatusCode.OK, new[] { Informe("investigativo", 9) });
        var vm = Vm();

        vm.PrepararEmisionCommand.Execute(TipoDeInforme.Investigativo);
        await vm.ConfirmarEmisionCommand.ExecuteAsync(null);

        Assert.Equal($"/casos/{CasoId}/informes/investigativo", _servidor.Peticiones.Single(p => p.Method == HttpMethod.Post).RequestUri!.AbsolutePath);
        Assert.StartsWith("Informe investigativo (SÓLO INVESTIGACIÓN, no es prueba) emitido y asentado en el asiento 9", vm.Aviso);
        Assert.Equal([9L], vm.Investigativos.Select(i => i.Secuencia));
    }

    [Theory]
    [InlineData(HttpStatusCode.Locked, "La cadena del caso no es confiable: no se emite nada pericial.")]
    [InlineData(HttpStatusCode.Forbidden, "El auditor no emite informes.")]
    [InlineData(HttpStatusCode.Conflict, "Otro asiento llegó a la cadena durante la emisión.")]
    public async Task Si_el_servidor_rechaza_la_emision_muestra_su_motivo_y_hay_que_volver_a_confirmar(HttpStatusCode estado, string detalle)
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/informes/pericial", _ => ServidorFalso.Problema(estado, "Rechazado", detalle));
        var vm = Vm();

        vm.PrepararEmisionCommand.Execute(TipoDeInforme.Pericial);
        await vm.ConfirmarEmisionCommand.ExecuteAsync(null);

        Assert.Equal(detalle, vm.Error);
        Assert.Null(vm.Aviso);
        Assert.Null(vm.EmisionPendiente);
        Assert.False(vm.Emitiendo);
    }

    [Theory]
    [InlineData(ArchivoDeInforme.Pdf, "pdf", 'p')]
    [InlineData(ArchivoDeInforme.Bundle, "bundle", 'b')]
    public async Task Descargar_escribe_el_archivo_y_dice_el_sha_256_que_verifico_el_servidor(ArchivoDeInforme archivo, string ruta, char hash)
    {
        var bytes = Enumerable.Range(0, 5_000).Select(i => (byte)i).ToArray();
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/informes/pericial/7/{ruta}",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var vm = Vm();
        var informe = Pericial();
        var destino = new DestinoEnMemoria();

        await vm.DescargarCommand.ExecuteAsync(new DescargaDeInforme(informe, archivo, new DestinoDeDescarga("x", () => Task.FromResult<Stream>(destino))));

        Assert.Equal(bytes, destino.ToArray());
        Assert.Equal($"Descargado en «x». El servidor verificó su SHA-256 {new string(hash, 64)} antes de entregarlo.", vm.Aviso);
    }

    [Fact]
    public async Task Si_el_servidor_rechaza_la_descarga_muestra_el_motivo_y_no_crea_el_archivo()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/informes/pericial/7/bundle",
            _ => ServidorFalso.Problema(HttpStatusCode.Locked, "Integridad comprometida", "El bundle no coincide con el hash asentado."));
        var vm = Vm();
        var abierto = false;

        await vm.DescargarCommand.ExecuteAsync(new DescargaDeInforme(Pericial(), ArchivoDeInforme.Bundle, new DestinoDeDescarga("x", () =>
        {
            abierto = true;
            return Task.FromResult<Stream>(new MemoryStream());
        })));

        Assert.Equal("El bundle no coincide con el hash asentado.", vm.Error);
        Assert.False(abierto);
    }

    [Fact]
    public void Sugiere_un_nombre_con_el_expediente_el_tipo_y_el_asiento()
    {
        var vm = Vm();
        var informe = Pericial();

        Assert.Equal("EXP-1-informe-pericial-7.pdf", vm.NombreSugerido(informe, ArchivoDeInforme.Pdf));
        Assert.Equal("EXP-1-informe-pericial-7-bundle.zip", vm.NombreSugerido(informe, ArchivoDeInforme.Bundle));
    }

    [Fact]
    public void Volver_lleva_al_mismo_caso_y_desde_el_caso_se_llega_a_los_informes()
    {
        _caso.VerInformesCommand.Execute(null);
        var vm = Assert.IsType<InformesViewModel>(_navegacion.Actual);
        Assert.Equal(CasoId, vm.Caso.Id);

        vm.VolverCommand.Execute(null);

        Assert.Same(_caso, _navegacion.Actual);
    }

    private static InformeEmitidoDto Pericial() => new(
        TipoDeInforme.Pericial, 7, DateTimeOffset.UtcNow, "sergio", 6,
        new ArchivoDelInformeDto(new string('p', 64), 62_056), new ArchivoDelInformeDto(new string('b', 64), 62_951), 2, 0, 0);
}
