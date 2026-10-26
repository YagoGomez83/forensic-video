using System.Text.Json;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Ia;
using Forensic.Domain.Comun;
using Forensic.Domain.Ia;

namespace Forensic.IntegrationTests;

/// <summary>
/// El gate de la Fase 4 con la detección: worker de IA real (imagen Docker, CPU) → cliente tipado →
/// servidor real. No se exige reproducibilidad byte a byte: se exige procedencia completa, vía
/// «sólo investigación» irreversible y que nada se cruce a lo admisible.
/// </summary>
public class CarrilDeIaDePuntaAPuntaTests(ServidorForense servidor, WorkerDeIaReal worker)
    : IClassFixture<ServidorForense>, IClassFixture<WorkerDeIaReal>
{
    private const int Umbral = 10;

    private async Task<ClienteForense> AnalistaAsync()
    {
        var nombre = $"ia-{Guid.NewGuid():N}"[..16];
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista, "la frase de la analista");
        servidor.CreateClient().Dispose(); // arranca el servidor de pruebas
        var cliente = ClienteForense.Crear(servidor.Server.BaseAddress, new SesionEnMemoria(), servidor.Server.CreateHandler());
        await cliente.IniciarSesionAsync(nombre, "la frase de la analista", CancellationToken.None);
        return cliente;
    }

    private ClienteWorkerIa Worker() => ClienteWorkerIa.Crear(worker.UrlBase);

    [Fact]
    public async Task Una_deteccion_real_queda_asentada_con_procedencia_completa_y_solo_investigacion()
    {
        var ct = CancellationToken.None;
        var cliente = await AnalistaAsync();
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Carril de IA", ct);
        var png = await worker.GenerarPngAsync(ancho: 320, alto: 240, semilla: 4);
        var original = await cliente.IngestarAsync(caso.Id, "cam1.png", new MemoryStream(png), png.Length, null, ct);
        // Lo que el analista ve en el visor: el contenido que el servidor entrega verificado.
        var contenido = await cliente.DescargarContenidoAsync(caso.Id, original.Id, ct);

        var analisis = await new OrquestadorDeAnalisisIa(cliente, Worker()).DetectarAsync(caso.Id, original.Id, contenido, Umbral, ct);

        // Procedencia: el modelo exacto (pesos por hash), los parámetros y el entorno real del worker.
        Assert.Equal(Forensic.Cliente.Api.ViaProcesamiento.SoloInvestigacion, analisis.Via);
        Assert.Equal(CatalogoDeIa.Deteccion, analisis.Analisis);
        Assert.Equal($$"""{"umbralConfianzaMilesimas":{{Umbral}}}""", analisis.Parametros);
        var permitido = Assert.Single(CatalogoDeIa.Modelos(CatalogoDeIa.Deteccion));
        Assert.Equal(new ModeloDeIaDto(permitido.Id, permitido.Version, permitido.Sha256.Hex), analisis.Modelo);
        var entorno = JsonDocument.Parse(analisis.Entorno).RootElement;
        Assert.Equal("CPUExecutionProvider", entorno.GetProperty("proveedor").GetString());
        foreach (var clave in new[] { "worker", "python", "onnxruntime", "opencv", "numpy", "plataforma", "gpu", "postproceso" })
            Assert.True(entorno.TryGetProperty(clave, out _), $"Falta '{clave}' en el entorno.");
        // Con esta imagen (ruido, semilla 4) y umbral 10, el modelo en CPU reporta pistas: el test no es vacío.
        Assert.NotEmpty(analisis.Hallazgos);
        Assert.All(analisis.Hallazgos, h =>
        {
            Assert.Equal(Forensic.Cliente.Api.ViaProcesamiento.SoloInvestigacion, h.Via);
            Assert.InRange(h.Confianza, Umbral / 1000m, 1m);
            Assert.True(h.Region.X + h.Region.Ancho <= 320 && h.Region.Y + h.Region.Alto <= 240);
            Assert.Equal(h.Confianza, JsonDocument.Parse(h.Crudo).RootElement.GetProperty("puntaje").GetDecimal());
        });

        // Encadenado en la custodia, y la cadena verifica.
        var custodia = await cliente.ExportarCustodiaAsync(caso.Id, ct);
        Assert.True(custodia.Verificacion.EsIntegra, custodia.Verificacion.Motivo);
        Assert.Equal(["CasoAbierto", "EvidenciaIngestada", "EvidenciaAccedida", "HallazgosRegistrados"], custodia.Asientos.Select(a => a.Tipo));
        var detalle = JsonDocument.Parse(custodia.Asientos[^1].Detalle).RootElement;
        Assert.Equal(original.Hash, detalle.GetProperty("entrada").GetProperty("sha256").GetString());
        Assert.Equal(AnalisisIa.OrigenDeLaProcedencia, detalle.GetProperty("procedencia").GetProperty("origen").GetString());
        Assert.Equal(analisis.Hallazgos.Count, detalle.GetProperty("hallazgos").GetArrayLength());

        // Nada se cruza: analizar con IA no toca la evidencia, que sigue admisible y sin derivados.
        var evidencia = Assert.Single(await cliente.ListarEvidenciasAsync(caso.Id, ct));
        Assert.Equal((original.Id, Forensic.Cliente.Api.ViaProcesamiento.AdmisibleEnJuicio), (evidencia.Id, evidencia.Via));
    }

    [Fact]
    public async Task Un_reporte_del_worker_sobre_otro_contenido_no_se_asienta()
    {
        var ct = CancellationToken.None;
        var cliente = await AnalistaAsync();
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Carril de IA", ct);
        var png = await worker.GenerarPngAsync(ancho: 64, alto: 48, semilla: 5);
        var original = await cliente.IngestarAsync(caso.Id, "cam1.png", new MemoryStream(png), png.Length, null, ct);
        var otra = await worker.GenerarPngAsync(ancho: 64, alto: 48, semilla: 6);

        var reporte = await Worker().DetectarAsync(otra, Umbral, ct);
        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => cliente.RegistrarAnalisisIaAsync(caso.Id, original.Id, reporte, ct));

        Assert.Equal(TipoDeError.Rechazado, ex.Tipo);
        Assert.Contains("otro contenido", ex.Message);
        Assert.Equal(["CasoAbierto", "EvidenciaIngestada"], (await cliente.ExportarCustodiaAsync(caso.Id, ct)).Asientos.Select(a => a.Tipo));
    }

    [Fact]
    public async Task La_lista_blanca_del_servidor_es_la_de_los_pesos_que_el_worker_verifico()
    {
        using var http = new HttpClient { BaseAddress = worker.UrlBase };
        var modelos = JsonDocument.Parse(await http.GetStringAsync("v1/entorno")).RootElement.GetProperty("modelos");

        var delWorker = modelos.EnumerateObject()
            .Select(m => new ModeloDeIa(m.Name, m.Value.GetProperty("version").GetString()!, HashSha256.Parse(m.Value.GetProperty("sha256").GetString()!)))
            .OrderBy(m => m.Id)
            .ToList();
        var delServidor = CatalogoDeIa.Tipos.SelectMany(CatalogoDeIa.Modelos).Distinct().OrderBy(m => m.Id).ToList();

        Assert.Equal(delServidor, delWorker);
    }
}
