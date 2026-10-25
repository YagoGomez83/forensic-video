using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Forensic.Application;
using Forensic.Application.Custodia;
using Forensic.Application.Evidencias;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Forensic.IntegrationTests;

/// <summary>
/// El worker de ingesta re-hashea los mismos bytes crudos que guardó el servidor. Distingue tres
/// caminos: el worker discrepa (evento de seguridad, 502), rechaza el contenido (422) o no está
/// (la ingesta procede con el control cruzado pendiente).
/// </summary>
public class ControlCruzadoDeIngestaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private const string RechazoDelWorker = """{"detail":"contenido no admisible"}""";

    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);
    private readonly HttpClient _sofia = servidor.ClienteDe("sofia", Rol.Supervisor);
    private readonly HttpClient _aurelio = servidor.ClienteDe("aurelio", Rol.Auditor);

    /// <summary>El mismo servidor (misma base y almacén) con otro worker de ingesta y su log técnico capturado.</summary>
    private sealed record ServidorAlternativo(WebApplicationFactory<Program> Fabrica, HttpClient Ana, RegistroDeLogs Logs)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Fabrica.DisposeAsync();
    }

    private ServidorAlternativo ConWorker(Action<IServiceCollection> worker, string urlWorker = "http://worker-falso")
    {
        var logs = new RegistroDeLogs();
        var otro = servidor.WithWebHostBuilder(b =>
        {
            b.UseSetting("WorkerIngesta:UrlBase", urlWorker);
            b.ConfigureLogging(l => l.AddProvider(logs));
            b.ConfigureTestServices(worker);
        });
        return new ServidorAlternativo(otro, otro.CreateClient().ComoActor("ana", Rol.Analista), logs);
    }

    private ServidorAlternativo ConWorkerDiscrepante() =>
        ConWorker(s => s.AddSingleton<IWorkerDeIngesta>(new WorkerDeIngestaDoble(discrepar: true)));

    /// <summary>El cliente HTTP real contra un puerto donde no escucha nadie.</summary>
    private ServidorAlternativo SinWorker() =>
        ConWorker(s => s.AddHttpClient<IWorkerDeIngesta, WorkerDeIngestaHttp>(), "http://127.0.0.1:9");

    private static JsonElement Detalle(ExportDeCustodia export, int indice) =>
        JsonDocument.Parse(export.Asientos[indice].Detalle).RootElement;

    /// <summary>Ingesta con el worker caído: la evidencia queda con el control cruzado pendiente.</summary>
    private async Task<EvidenciaDto> IngestarConCrucePendienteAsync(Guid casoId)
    {
        await using var sinWorker = SinWorker();
        return await (await sinWorker.Ana.IngestarAsync(casoId, RandomNumberGenerator.GetBytes(2048))).LeerAsync<EvidenciaDto>();
    }

    private static Task<HttpResponseMessage> CompletarCruceAsync(HttpClient cliente, Guid casoId, Guid evidenciaId) =>
        cliente.PostAsync($"/casos/{casoId}/evidencias/{evidenciaId}/control-cruzado", null);

    private async Task CerrarAsync(Guid casoId) =>
        (await _sofia.PostAsync($"/casos/{casoId}/cerrar", null)).EnsureSuccessStatusCode();

    private async Task<List<EvidenciaDto>> EvidenciasAsync(Guid casoId) =>
        await (await _ana.GetAsync($"/casos/{casoId}/evidencias")).LeerAsync<List<EvidenciaDto>>();

    [Fact]
    public async Task La_ingesta_asienta_el_control_cruzado_y_los_metadatos()
    {
        var caso = await _ana.AbrirCasoAsync();

        var evidencia = await (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(2048))).LeerAsync<EvidenciaDto>();

        var detalle = Detalle(await _ana.ExportarAsync(caso.Id), 1);
        var control = detalle.GetProperty("controlCruzado");
        Assert.Equal("coincide", control.GetProperty("estado").GetString());
        Assert.Equal(evidencia.Hash, control.GetProperty("sha256Worker").GetString());
        Assert.Equal("doble", detalle.GetProperty("metadatos").GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("motivoSinMetadatos").ValueKind);
    }

    [Fact]
    public async Task Una_divergencia_del_worker_rechaza_la_ingesta_con_502_y_log_critico()
    {
        var caso = await _ana.AbrirCasoAsync();
        await using var discrepante = ConWorkerDiscrepante();

        var r = await discrepante.Ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096));

        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.True(discrepante.Logs.HayCriticos);
        Assert.Empty(await EvidenciasAsync(caso.Id));
        var export = await _ana.ExportarAsync(caso.Id);
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        Assert.Equal(["CasoAbierto"], export.Asientos.Select(a => a.Tipo));
    }

    [Fact]
    public async Task Un_rechazo_de_contenido_del_worker_es_422_y_no_un_evento_critico()
    {
        var caso = await _ana.AbrirCasoAsync();
        // El cliente HTTP real, con un worker que responde 422 desde su /v1/analizar.
        await using var rechaza = ConWorker(s => s.AddHttpClient<IWorkerDeIngesta, WorkerDeIngestaHttp>()
            .ConfigurePrimaryHttpMessageHandler(() => new RespuestaFijaDelWorker(HttpStatusCode.UnprocessableEntity, RechazoDelWorker)));

        var r = await rechaza.Ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.False(rechaza.Logs.HayCriticos);
        Assert.Empty(await EvidenciasAsync(caso.Id));
    }

    [Fact]
    public async Task Una_evidencia_que_ffprobe_no_lee_se_custodia_igual()
    {
        var caso = await _ana.AbrirCasoAsync();
        await using var sinMetadatos = ConWorker(s =>
            s.AddSingleton<IWorkerDeIngesta>(new WorkerDeIngestaDoble(motivoSinMetadatos: "formato no reconocido")));

        var r = await sinMetadatos.Ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(2048), "export.dav");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var detalle = Detalle(await _ana.ExportarAsync(caso.Id), 1);
        Assert.Equal("coincide", detalle.GetProperty("controlCruzado").GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("metadatos").ValueKind);
        Assert.Equal("formato no reconocido", detalle.GetProperty("motivoSinMetadatos").GetString());
    }

    [Fact]
    public async Task Sin_worker_la_ingesta_procede_y_el_control_cruzado_se_completa_despues()
    {
        var caso = await _ana.AbrirCasoAsync();
        var bytes = RandomNumberGenerator.GetBytes(2048);
        EvidenciaDto evidencia;
        await using (var sinWorker = SinWorker())
        {
            evidencia = await (await sinWorker.Ana.IngestarAsync(caso.Id, bytes)).LeerAsync<EvidenciaDto>();
            Assert.False(sinWorker.Logs.HayCriticos);
        }

        Assert.Equal(HashSha256.Calcular(bytes).Hex, evidencia.Hash);
        var pendiente = Detalle(await _ana.ExportarAsync(caso.Id), 1);
        Assert.Equal("pendiente", pendiente.GetProperty("controlCruzado").GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, pendiente.GetProperty("metadatos").ValueKind);
        Assert.Equal("worker de ingesta no disponible", pendiente.GetProperty("motivoSinMetadatos").GetString());

        // Con el worker de vuelta, el cruce se completa en un asiento nuevo: el de ingesta es inmutable.
        var url = $"/casos/{caso.Id}/evidencias/{evidencia.Id}/control-cruzado";
        Assert.Equal(HttpStatusCode.OK, (await _ana.PostAsync(url, null)).StatusCode);

        var export = await _ana.ExportarAsync(caso.Id);
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        Assert.Equal(["CasoAbierto", "EvidenciaIngestada", "ControlCruzadoCompletado"], export.Asientos.Select(a => a.Tipo));
        Assert.Equal(evidencia.Id, export.Asientos[2].EvidenciaId);
        var completado = Detalle(export, 2);
        Assert.Equal("coincide", completado.GetProperty("resultado").GetString());
        Assert.Equal(evidencia.Hash, completado.GetProperty("sha256Worker").GetString());
        Assert.Equal("doble", completado.GetProperty("metadatos").GetProperty("version").GetString());

        // Ya no queda nada pendiente.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _ana.PostAsync(url, null)).StatusCode);
    }

    [Fact]
    public async Task Una_divergencia_al_completar_el_control_cruzado_se_asienta_y_es_502()
    {
        var caso = await _ana.AbrirCasoAsync();
        EvidenciaDto evidencia;
        await using (var sinWorker = SinWorker())
            evidencia = await (await sinWorker.Ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(2048))).LeerAsync<EvidenciaDto>();

        await using var discrepante = ConWorkerDiscrepante();
        var r = await discrepante.Ana.PostAsync($"/casos/{caso.Id}/evidencias/{evidencia.Id}/control-cruzado", null);

        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.True(discrepante.Logs.HayCriticos);
        var export = await _ana.ExportarAsync(caso.Id);
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        Assert.Equal("ControlCruzadoCompletado", export.Asientos[^1].Tipo);
        var detalle = Detalle(export, 2);
        Assert.Equal("divergente", detalle.GetProperty("resultado").GetString());
        Assert.NotEqual(evidencia.Hash, detalle.GetProperty("sha256Worker").GetString());
        // La evidencia custodiada no se toca: su integridad la garantiza el almacén, no el worker.
        Assert.Equal(HttpStatusCode.OK, (await _ana.GetAsync($"/casos/{caso.Id}/evidencias/{evidencia.Id}/contenido")).StatusCode);
    }

    [Fact]
    public async Task Un_supervisor_completa_un_cruce_pendiente_con_el_caso_cerrado()
    {
        var caso = await _ana.AbrirCasoAsync();
        var evidencia = await IngestarConCrucePendienteAsync(caso.Id);
        await CerrarAsync(caso.Id);

        Assert.Equal(HttpStatusCode.OK, (await CompletarCruceAsync(_sofia, caso.Id, evidencia.Id)).StatusCode);

        var export = await _sofia.ExportarAsync(caso.Id);
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        Assert.Equal(["CasoAbierto", "EvidenciaIngestada", "CasoCerrado", "ControlCruzadoCompletado"], export.Asientos.Select(a => a.Tipo));
        Assert.Equal("sofia", export.Asientos[^1].Actor);
        Assert.Equal("coincide", Detalle(export, 3).GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task Con_el_caso_cerrado_el_analista_asignado_ya_no_completa_el_cruce()
    {
        var caso = await _ana.AbrirCasoAsync();
        var evidencia = await IngestarConCrucePendienteAsync(caso.Id);
        await CerrarAsync(caso.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await CompletarCruceAsync(_ana, caso.Id, evidencia.Id)).StatusCode);
        Assert.Equal(1, (await _ana.ExportarAsync(caso.Id)).CrucesPendientes);
    }

    [Fact]
    public async Task Un_auditor_no_completa_cruces_ni_con_el_caso_abierto_ni_cerrado()
    {
        var caso = await _ana.AbrirCasoAsync();
        var evidencia = await IngestarConCrucePendienteAsync(caso.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await CompletarCruceAsync(_aurelio, caso.Id, evidencia.Id)).StatusCode);
        await CerrarAsync(caso.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await CompletarCruceAsync(_aurelio, caso.Id, evidencia.Id)).StatusCode);
        Assert.Equal(1, (await _ana.ExportarAsync(caso.Id)).CrucesPendientes);
    }

    [Fact]
    public async Task Los_cruces_pendientes_no_bloquean_el_cierre_y_quedan_a_la_vista()
    {
        var caso = await _ana.AbrirCasoAsync();
        await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(2048));
        var primera = await IngestarConCrucePendienteAsync(caso.Id);
        var segunda = await IngestarConCrucePendienteAsync(caso.Id);
        Assert.Equal(2, (await _ana.ExportarAsync(caso.Id)).CrucesPendientes);

        await CerrarAsync(caso.Id);

        // El cierre deja asentado con cuántos cruces pendientes se cerró el caso.
        var alCerrar = await _sofia.ExportarAsync(caso.Id);
        Assert.Equal("CasoCerrado", alCerrar.Asientos[^1].Tipo);
        Assert.Equal(2, Detalle(alCerrar, alCerrar.Asientos.Count - 1).GetProperty("crucesPendientes").GetInt32());

        (await CompletarCruceAsync(_sofia, caso.Id, primera.Id)).EnsureSuccessStatusCode();
        Assert.Equal(1, (await _sofia.ExportarAsync(caso.Id)).CrucesPendientes);
        (await CompletarCruceAsync(_sofia, caso.Id, segunda.Id)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await _sofia.ExportarAsync(caso.Id)).CrucesPendientes);
    }

    [Fact]
    public async Task El_listado_de_evidencias_marca_las_que_tienen_el_cruce_pendiente()
    {
        var caso = await _ana.AbrirCasoAsync();
        var cruzada = await (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(2048))).LeerAsync<EvidenciaDto>();
        var pendiente = await IngestarConCrucePendienteAsync(caso.Id);
        Assert.False(cruzada.ControlCruzadoPendiente);
        Assert.True(pendiente.ControlCruzadoPendiente);

        var antes = (await EvidenciasAsync(caso.Id)).ToDictionary(e => e.Id, e => e.ControlCruzadoPendiente);
        Assert.False(antes[cruzada.Id]);
        Assert.True(antes[pendiente.Id]);

        (await CompletarCruceAsync(_ana, caso.Id, pendiente.Id)).EnsureSuccessStatusCode();

        Assert.All(await EvidenciasAsync(caso.Id), e => Assert.False(e.ControlCruzadoPendiente));
    }

    [Theory]
    [InlineData("sha256")]
    [InlineData("tamanoBytes")]
    [InlineData("ffprobe")]
    [InlineData("motivoSinMetadatos")]
    public async Task Una_respuesta_del_worker_sin_un_campo_es_invalida_y_no_una_divergencia(string campoAusente)
    {
        var caso = await _ana.AbrirCasoAsync();
        var bytes = RandomNumberGenerator.GetBytes(2048);
        // Respuesta correcta para estos bytes, salvo por el campo que falta.
        var respuesta = new Dictionary<string, object?>
        {
            ["sha256"] = HashSha256.Calcular(bytes).Hex,
            ["tamanoBytes"] = bytes.Length,
            ["ffprobe"] = WorkerDeIngestaDoble.Metadatos,
            ["motivoSinMetadatos"] = null,
        };
        respuesta.Remove(campoAusente);
        await using var incompleto = ConWorker(s => s.AddHttpClient<IWorkerDeIngesta, WorkerDeIngestaHttp>()
            .ConfigurePrimaryHttpMessageHandler(() => new RespuestaFijaDelWorker(HttpStatusCode.OK, JsonSerializer.Serialize(respuesta))));

        var evidencia = await (await incompleto.Ana.IngestarAsync(caso.Id, bytes)).LeerAsync<EvidenciaDto>();

        Assert.False(incompleto.Logs.HayCriticos);
        Assert.True(evidencia.ControlCruzadoPendiente);
        Assert.Equal("pendiente", Detalle(await _ana.ExportarAsync(caso.Id), 1).GetProperty("controlCruzado").GetProperty("estado").GetString());
    }
}
