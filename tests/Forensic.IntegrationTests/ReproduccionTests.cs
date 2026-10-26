using System.Net;
using System.Security.Cryptography;
using Forensic.Application.Evidencias;
using Forensic.Application.Operaciones;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>
/// El gate de la Fase 2: desde el asiento de un derivado se re-ejecuta la operación y el hash de
/// salida coincide byte a byte. Acá con el doble (la mecánica); contra OpenCV real, en
/// <see cref="ReproduccionConOpenCvTests"/>.
/// </summary>
public class ReproduccionTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    private sealed record ServidorAlternativo(WebApplicationFactory<Program> Fabrica, HttpClient Ana, RegistroDeLogs Logs) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Fabrica.DisposeAsync();
    }

    private ServidorAlternativo ConWorker(WorkerDeProcesamientoDoble.Modo modo)
    {
        var logs = new RegistroDeLogs();
        var otro = servidor.WithWebHostBuilder(b =>
        {
            b.ConfigureLogging(l => l.AddProvider(logs));
            b.ConfigureTestServices(s => s.AddSingleton<IWorkerDeProcesamiento>(new WorkerDeProcesamientoDoble(modo)));
        });
        return new ServidorAlternativo(otro, otro.CreateClient().ComoActor("ana", Rol.Analista), logs);
    }

    private async Task<(Guid CasoId, EvidenciaDto Original, EvidenciaDto Derivado)> CasoConRecorteAsync()
    {
        var caso = await _ana.AbrirCasoAsync();
        var original = await (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096), "cam1.png")).LeerAsync<EvidenciaDto>();
        var aplicada = await (await _ana.AplicarOperacionAsync(caso.Id, original.Id, "opencv.recorte/v1", new { x = 1, y = 2, ancho = 3, alto = 4 }))
            .LeerAsync<OperacionAplicadaDto>();
        return (caso.Id, original, aplicada.Derivado);
    }

    [Fact]
    public async Task Reproducir_desde_el_asiento_da_el_mismo_hash_y_queda_asentado()
    {
        var (casoId, _, derivado) = await CasoConRecorteAsync();

        var r = await _ana.ReproducirAsync(casoId, derivado.Id);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var resultado = await r.LeerAsync<ResultadoDeReproduccion>();
        Assert.True(resultado.Reproducido);
        Assert.True(resultado.EntornoCoincide);
        Assert.Equal(derivado.Hash, resultado.Sha256Esperado);
        Assert.Equal(derivado.Hash, resultado.Sha256Obtenido);
        Assert.Equal(resultado.Sha256PixelesEsperado, resultado.Sha256PixelesObtenido);

        var export = await _ana.ExportarAsync(casoId);
        Assert.True(export.Verificacion.EsIntegra, export.Verificacion.Motivo);
        var asiento = export.Asientos[^1];
        Assert.Equal((nameof(TipoAsiento.OperacionReproducida), derivado.Id), (asiento.Tipo, asiento.EvidenciaId));
        Assert.Contains("\"reproducido\":true", asiento.Detalle);
        Assert.Contains("\"formato\":\"forensic-video/reproduccion/v1\"", asiento.Detalle);
        // Reproducir no crea un derivado nuevo: si reproduce, es idéntico al que ya está custodiado.
        Assert.Equal(2, (await _ana.EvidenciasAsync(casoId)).Count);
    }

    [Fact]
    public async Task Un_auditor_puede_reproducir_y_un_analista_no_asignado_no()
    {
        var (casoId, _, derivado) = await CasoConRecorteAsync();

        Assert.Equal(HttpStatusCode.OK, (await servidor.ClienteDe("aurelio", Rol.Auditor).ReproducirAsync(casoId, derivado.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await servidor.ClienteDe("beto", Rol.Analista).ReproducirAsync(casoId, derivado.Id)).StatusCode);
    }

    [Fact]
    public async Task Si_el_entorno_ya_no_reproduce_se_informa_y_se_asienta_sin_evento_critico()
    {
        var (casoId, _, derivado) = await CasoConRecorteAsync();
        await using var deriva = ConWorker(WorkerDeProcesamientoDoble.Modo.Deriva);

        var r = await deriva.Ana.ReproducirAsync(casoId, derivado.Id);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var resultado = await r.LeerAsync<ResultadoDeReproduccion>();
        Assert.False(resultado.Reproducido);
        Assert.False(resultado.EntornoCoincide);
        Assert.NotEqual(resultado.Sha256Esperado, resultado.Sha256Obtenido);
        Assert.False(deriva.Logs.HayCriticos);
        Assert.Contains(deriva.Logs.Entradas, e => e.Nivel == LogLevel.Error);
        Assert.Contains("\"reproducido\":false", (await _ana.ExportarAsync(casoId)).Asientos[^1].Detalle);
    }

    [Fact]
    public async Task Una_divergencia_del_worker_al_reproducir_es_502_con_log_critico()
    {
        var (casoId, _, derivado) = await CasoConRecorteAsync();
        await using var discrepante = ConWorker(WorkerDeProcesamientoDoble.Modo.DiscrepaSalida);

        Assert.Equal(HttpStatusCode.BadGateway, (await discrepante.Ana.ReproducirAsync(casoId, derivado.Id)).StatusCode);
        Assert.True(discrepante.Logs.HayCriticos);
    }

    [Fact]
    public async Task Un_original_no_se_reproduce()
    {
        var (casoId, original, _) = await CasoConRecorteAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _ana.ReproducirAsync(casoId, original.Id)).StatusCode);
    }

    [Fact]
    public async Task Con_la_cadena_manipulada_no_se_reproduce()
    {
        var (casoId, _, derivado) = await CasoConRecorteAsync();
        await using (var conexion = new NpgsqlConnection(servidor.CadenaDeConexion))
        {
            await conexion.OpenAsync();
            // Un atacante con acceso a la base cambia los parámetros asentados, saltando los triggers.
            await using var cmd = new NpgsqlCommand($"""
                ALTER TABLE "AsientosDeCustodia" DISABLE TRIGGER USER;
                UPDATE "AsientosDeCustodia" SET "Detalle" = replace("Detalle", '"x":1', '"x":0')
                    WHERE "CasoId" = '{casoId}' AND "Tipo" = 'OperacionAplicada';
                ALTER TABLE "AsientosDeCustodia" ENABLE TRIGGER USER;
                """, conexion);
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.Locked, (await _ana.ReproducirAsync(casoId, derivado.Id)).StatusCode);
    }
}
