using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Forensic.Api.Controllers;
using Forensic.Application;
using Forensic.Application.Evidencias;
using Forensic.Application.Operaciones;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Operaciones;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>
/// Aplicar una operación: autoriza, relee la entrada verificada, la manda al worker, guarda la salida
/// (el almacén la hashea), cruza los hashes y asienta operación, derivado y asiento juntos.
/// </summary>
public class AplicarOperacionTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private const string Recorte = "opencv.recorte/v1";

    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    private sealed record ServidorAlternativo(WebApplicationFactory<Program> Fabrica, HttpClient Ana, RegistroDeLogs Logs, WorkerDeProcesamientoDoble Worker)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Fabrica.DisposeAsync();
    }

    private ServidorAlternativo ConWorker(WorkerDeProcesamientoDoble.Modo modo)
    {
        var logs = new RegistroDeLogs();
        var worker = new WorkerDeProcesamientoDoble(modo);
        var otro = servidor.WithWebHostBuilder(b =>
        {
            b.ConfigureLogging(l => l.AddProvider(logs));
            b.ConfigureTestServices(s => s.AddSingleton<IWorkerDeProcesamiento>(worker));
        });
        return new ServidorAlternativo(otro, otro.CreateClient().ComoActor("ana", Rol.Analista), logs, worker);
    }

    private async Task<(Guid CasoId, EvidenciaDto Original, byte[] Bytes)> CasoConOriginalAsync()
    {
        var caso = await _ana.AbrirCasoAsync();
        var bytes = RandomNumberGenerator.GetBytes(4096);
        var original = await (await _ana.IngestarAsync(caso.Id, bytes, "cam1.png")).LeerAsync<EvidenciaDto>();
        return (caso.Id, original, bytes);
    }

    private static object RecorteDe(int x, int y, int ancho, int alto) => new { x, y, ancho, alto };

    [Fact]
    public async Task Aplicar_un_recorte_crea_un_derivado_encadenado_y_asienta_todo_lo_necesario_para_reproducirlo()
    {
        var (casoId, original, bytes) = await CasoConOriginalAsync();

        var r = await _ana.AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(5, 10, 30, 20));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var aplicada = await r.LeerAsync<OperacionAplicadaDto>();
        var derivado = aplicada.Derivado;
        var esperado = WorkerDeProcesamientoDoble.SalidaPara(Recorte, """{"alto":20,"ancho":30,"x":5,"y":10}""", bytes);
        Assert.Equal(HashSha256.Calcular(esperado).Hex, derivado.Hash);
        Assert.Equal((original.Id, ViaProcesamiento.AdmisibleEnJuicio, "cam1-recorte.png"), (derivado.PadreId, derivado.Via, derivado.NombreOriginal));

        // El derivado se descarga verificado, y el original sigue intacto.
        Assert.Equal(esperado, await (await _ana.GetAsync($"/casos/{casoId}/evidencias/{derivado.Id}/contenido")).Content.ReadAsByteArrayAsync());
        Assert.Equal(bytes, await (await _ana.GetAsync($"/casos/{casoId}/evidencias/{original.Id}/contenido")).Content.ReadAsByteArrayAsync());

        var export = await _ana.ExportarAsync(casoId);
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        var asiento = export.Asientos.Single(a => a.Tipo == nameof(TipoAsiento.OperacionAplicada));
        Assert.Equal(derivado.Id, asiento.EvidenciaId);
        Assert.Contains("\"formato\":\"forensic-video/operacion/v1\"", asiento.Detalle);
        Assert.Contains("\"parametros\":{\"alto\":20,\"ancho\":30,\"x\":5,\"y\":10}", asiento.Detalle);
        Assert.Contains($"\"entorno\":{WorkerDeProcesamientoDoble.Entorno}", asiento.Detalle);
        Assert.Contains(original.Hash, asiento.Detalle);
    }

    [Fact]
    public async Task Un_derivado_de_un_derivado_hereda_la_via_y_encadena_al_padre_inmediato()
    {
        var (casoId, original, _) = await CasoConOriginalAsync();
        var primero = (await (await _ana.AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(0, 0, 40, 40))).LeerAsync<OperacionAplicadaDto>()).Derivado;

        var segundo = (await (await _ana.AplicarOperacionAsync(casoId, primero.Id, Recorte, RecorteDe(1, 1, 10, 10))).LeerAsync<OperacionAplicadaDto>()).Derivado;

        Assert.Equal(primero.Id, segundo.PadreId);
        Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, segundo.Via);
        Assert.True((await _ana.ExportarAsync(casoId)).Verificacion.EsIntegra);
    }

    [Theory]
    [InlineData("opencv.recorte/v1", """{"x":0,"y":0,"ancho":0,"alto":1}""")]
    [InlineData("opencv.recorte/v1", """{"x":0,"y":0,"ancho":1,"alto":1,"extra":true}""")]
    [InlineData("realesrgan.x4/v1", """{}""")]
    public async Task Una_operacion_invalida_se_rechaza_con_422_sin_llamar_al_worker(string tipo, string parametros)
    {
        var (casoId, original, _) = await CasoConOriginalAsync();
        await using var conDoble = ConWorker(WorkerDeProcesamientoDoble.Modo.Correcto);

        var r = await conDoble.Ana.AplicarOperacionAsync(casoId, original.Id, tipo, System.Text.Json.JsonDocument.Parse(parametros).RootElement);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(0, conDoble.Worker.Llamadas);
    }

    [Theory]
    [InlineData(WorkerDeProcesamientoDoble.Modo.DiscrepaEntrada)]
    [InlineData(WorkerDeProcesamientoDoble.Modo.DiscrepaSalida)]
    public async Task Una_divergencia_del_worker_es_502_con_log_critico_y_no_crea_derivado(WorkerDeProcesamientoDoble.Modo modo)
    {
        var (casoId, original, _) = await CasoConOriginalAsync();
        await using var discrepante = ConWorker(modo);

        var r = await discrepante.Ana.AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(0, 0, 1, 1));

        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.True(discrepante.Logs.HayCriticos);
        Assert.Equal([original.Id], (await _ana.EvidenciasAsync(casoId)).Select(e => e.Id));
        Assert.DoesNotContain((await _ana.ExportarAsync(casoId)).Asientos, a => a.Tipo == nameof(TipoAsiento.OperacionAplicada));
    }

    [Fact]
    public async Task Si_el_worker_rechaza_el_contenido_es_422_y_si_no_responde_503_sin_derivado()
    {
        var (casoId, original, _) = await CasoConOriginalAsync();
        await using var rechaza = ConWorker(WorkerDeProcesamientoDoble.Modo.Rechaza);
        await using var caido = ConWorker(WorkerDeProcesamientoDoble.Modo.Caido);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await rechaza.Ana.AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(0, 0, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await caido.Ana.AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(0, 0, 1, 1))).StatusCode);
        Assert.False(rechaza.Logs.HayCriticos || caido.Logs.HayCriticos);
        Assert.Single(await _ana.EvidenciasAsync(casoId));
    }

    [Fact]
    public async Task Solo_el_analista_asignado_con_el_caso_abierto_aplica_operaciones()
    {
        var (casoId, original, _) = await CasoConOriginalAsync();

        foreach (var (usuario, rol) in new[] { ("beto", Rol.Analista), ("sofia", Rol.Supervisor), ("aurelio", Rol.Auditor) })
            Assert.Equal(HttpStatusCode.Forbidden,
                (await servidor.ClienteDe(usuario, rol).AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(0, 0, 1, 1))).StatusCode);
        (await servidor.ClienteDe("sofia", Rol.Supervisor).PostAsync($"/casos/{casoId}/cerrar", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await _ana.AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(0, 0, 1, 1))).StatusCode);
    }

    [Fact]
    public async Task Las_operaciones_registradas_son_inmutables()
    {
        var (casoId, original, _) = await CasoConOriginalAsync();
        (await _ana.AplicarOperacionAsync(casoId, original.Id, Recorte, RecorteDe(0, 0, 2, 2))).EnsureSuccessStatusCode();
        await using var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();

        await using var cmd = new NpgsqlCommand("""UPDATE "OperacionesForenses" SET "Parametros" = '{}'""", conexion);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Contains("append-only", ex.MessageText);
    }
}
