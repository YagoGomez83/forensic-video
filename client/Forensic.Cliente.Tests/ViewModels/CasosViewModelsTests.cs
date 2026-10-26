using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo;
using Forensic.Cliente.Nucleo.Casos;
using Forensic.Cliente.Tests.Dobles;

namespace Forensic.Cliente.Tests.ViewModels;

public class CasosViewModelsTests
{
    internal static readonly Guid CasoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid EvidenciaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static object Caso(string numero = "EXP-1") => new
    {
        id = CasoId, numeroExpediente = numero, descripcion = "Robo", estado = "Abierto",
        abiertoEn = DateTimeOffset.UtcNow, abiertoPor = "lucia", analistasAsignados = new[] { "lucia" },
    };

    internal static object Evidencia(Guid? id = null, Guid? padre = null, string nombre = "cam1.png", string via = "AdmisibleEnJuicio") => new
    {
        id = id ?? EvidenciaId, casoId = CasoId, hash = new string('a', 64), tamanoBytes = 2048, nombreOriginal = nombre,
        via, padreId = padre, ingestadaEn = DateTimeOffset.UtcNow, ingestadaPor = "lucia", controlCruzadoPendiente = false,
    };

    internal static object Export(bool integra = true) => new
    {
        formato = "forensic-video/custodia/v1", generadoEn = DateTimeOffset.UtcNow, generadoPor = "lucia", caso = Caso(),
        asientos = new object[]
        {
            new { secuencia = 1, id = Guid.NewGuid(), tipo = "CasoAbierto", evidenciaId = (Guid?)null, detalle = "{}", actor = "lucia",
                  timestamp = DateTimeOffset.UtcNow, hashAnterior = new string('0', 64), hash = new string('1', 64) },
            new { secuencia = 2, id = Guid.NewGuid(), tipo = "EvidenciaIngestada", evidenciaId = (Guid?)EvidenciaId, detalle = "{}", actor = "lucia",
                  timestamp = DateTimeOffset.UtcNow, hashAnterior = new string('1', 64), hash = new string('2', 64) },
        },
        verificacion = integra
            ? new { esIntegra = true, cantidadAsientos = 2, hashFinal = (string?)new string('2', 64), secuenciaRota = (long?)null, motivo = (string?)null }
            : new { esIntegra = false, cantidadAsientos = 2, hashFinal = (string?)null, secuenciaRota = (long?)2, motivo = (string?)"El hash del asiento no coincide." },
        crucesPendientes = 0,
    };

    private readonly ServidorFalso _servidor = new();
    private readonly NavegacionRegistrada _navegacion = new();
    private readonly ClienteForense _api;

    public CasosViewModelsTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task Cargar_muestra_los_casos_que_devuelve_el_servidor()
    {
        _servidor.En(HttpMethod.Get, "/casos", HttpStatusCode.OK, new[] { Caso("EXP-1"), Caso("EXP-2") });
        var vm = new CasosViewModel(_api, _navegacion);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(["EXP-1", "EXP-2"], vm.Casos.Select(c => c.NumeroExpediente));
    }

    [Fact]
    public async Task Abrir_un_caso_nuevo_lo_crea_en_el_servidor_y_lo_muestra()
    {
        _servidor.En(HttpMethod.Post, "/casos", HttpStatusCode.Created, Caso("EXP-9"));
        var vm = new CasosViewModel(_api, _navegacion) { NuevoNumero = "EXP-9", NuevaDescripcion = "Robo" };

        await vm.AbrirNuevoCommand.ExecuteAsync(null);

        Assert.Contains("\"numeroExpediente\":\"EXP-9\"", _servidor.Cuerpos[^1]);
        Assert.Equal("EXP-9", Assert.IsType<CasoViewModel>(_navegacion.Actual).Caso.NumeroExpediente);
    }

    [Fact]
    public async Task Si_el_servidor_rechaza_el_caso_nuevo_muestra_su_motivo()
    {
        _servidor.En(HttpMethod.Post, "/casos", _ => ServidorFalso.Problema(HttpStatusCode.UnprocessableEntity, "Regla", "Ya existe un caso con ese número de expediente."));
        var vm = new CasosViewModel(_api, _navegacion) { NuevoNumero = "EXP-1" };

        await vm.AbrirNuevoCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un caso con ese número de expediente.", vm.Error);
        Assert.Null(_navegacion.Actual);
    }

    [Fact]
    public async Task El_detalle_del_caso_muestra_sus_evidencias_y_la_cadena_con_la_verificacion_del_servidor()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias", HttpStatusCode.OK, new[] { Evidencia() })
            .En(HttpMethod.Get, $"/casos/{CasoId}/custodia", HttpStatusCode.OK, Export());
        var vm = new CasoViewModel(_api, _navegacion, (await CasoDtoAsync())!);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal("cam1.png", Assert.Single(vm.Evidencias).NombreOriginal);
        Assert.Equal(["CasoAbierto", "EvidenciaIngestada"], vm.Asientos.Select(a => a.Tipo));
        Assert.True(vm.CadenaIntegra);
        Assert.Equal("Cadena íntegra: 2 asientos verificados por el servidor.", vm.EstadoDeLaCadena);
    }

    [Fact]
    public async Task Una_cadena_rota_se_muestra_con_el_asiento_y_el_motivo_que_da_el_servidor()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias", HttpStatusCode.OK, Array.Empty<object>())
            .En(HttpMethod.Get, $"/casos/{CasoId}/custodia", HttpStatusCode.OK, Export(integra: false));
        var vm = new CasoViewModel(_api, _navegacion, (await CasoDtoAsync())!);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.False(vm.CadenaIntegra);
        Assert.Equal("CADENA COMPROMETIDA en el asiento 2: El hash del asiento no coincide.", vm.EstadoDeLaCadena);
    }

    [Fact]
    public async Task Sin_acceso_al_caso_muestra_el_rechazo_del_servidor()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias", _ => ServidorFalso.Problema(HttpStatusCode.Forbidden, "Acceso denegado", "'beto' no tiene acceso al caso EXP-1."));
        var vm = new CasoViewModel(_api, _navegacion, (await CasoDtoAsync())!);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal("'beto' no tiene acceso al caso EXP-1.", vm.Error);
    }

    [Fact]
    public async Task Con_la_sesion_vencida_el_comando_termina_sin_excepcion_y_la_sesion_expira()
    {
        // Una excepción que escapa de un AsyncRelayCommand se relanza en la UI y cierra la app.
        _servidor.En(HttpMethod.Get, "/casos", _ => ServidorFalso.Problema(HttpStatusCode.Unauthorized, "No autenticado", "vencido"));
        var vm = new CasosViewModel(_api, _navegacion);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.False(_api.Sesion.Activa);
        Assert.Null(vm.Error);
    }

    private async Task<CasoDto?> CasoDtoAsync()
    {
        var servidor = new ServidorFalso().En(HttpMethod.Get, "/casos", HttpStatusCode.OK, new[] { Caso() });
        var api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), servidor);
        return (await api.ListarCasosAsync(CancellationToken.None))[0];
    }
}
