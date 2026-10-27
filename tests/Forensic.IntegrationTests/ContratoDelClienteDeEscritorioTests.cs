using System.Net.Http.Json;
using Forensic.Api.Controllers;
using Forensic.Cliente.Api;
using Forensic.Domain.Comun;

namespace Forensic.IntegrationTests;

/// <summary>
/// El cliente tipado del escritorio (<c>client/Forensic.Cliente.Api</c>) contra el servidor real. El
/// cliente define sus propios contratos a propósito; estos tests detectan cualquier desfasaje.
/// </summary>
public class ContratoDelClienteDeEscritorioTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private ClienteForense Cliente(ISesion? sesion = null)
    {
        servidor.CreateClient().Dispose(); // arranca el servidor de pruebas
        return ClienteForense.Crear(servidor.Server.BaseAddress, sesion ?? new SesionEnMemoria(), servidor.Server.CreateHandler());
    }

    private async Task<ClienteForense> AnalistaAsync(string nombre)
    {
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista, "la frase de la analista");
        var cliente = Cliente();
        await cliente.IniciarSesionAsync(nombre, "la frase de la analista", CancellationToken.None);
        return cliente;
    }

    [Fact]
    public async Task El_login_y_la_lista_de_casos_del_cliente_coinciden_con_el_servidor()
    {
        var nombre = $"cli-{Guid.NewGuid():N}"[..16];
        var cliente = await AnalistaAsync(nombre);
        var http = servidor.ClienteDe(nombre, Rol.Analista);
        var caso = await http.AbrirCasoAsync();

        var casos = await cliente.ListarCasosAsync(CancellationToken.None);

        var visto = Assert.Single(casos, c => c.Id == caso.Id);
        Assert.Equal((caso.NumeroExpediente, EstadoCaso.Abierto, nombre), (visto.NumeroExpediente, visto.Estado, visto.AbiertoPor));
        Assert.Equal([nombre], visto.AnalistasAsignados);
    }

    [Fact]
    public async Task Las_credenciales_malas_dan_el_error_generico_del_servidor()
    {
        var ex = await Assert.ThrowsAsync<ErrorDeApi>(async () => await Cliente().IniciarSesionAsync("nadie", "una contrasena cualquiera", CancellationToken.None));

        Assert.Equal(TipoDeError.CredencialesInvalidas, ex.Tipo);
        Assert.Equal("Usuario o contraseña incorrectos.", ex.Message);
    }

    [Fact]
    public async Task Un_token_que_el_servidor_ya_no_acepta_expira_la_sesion()
    {
        var sesion = new SesionEnMemoria();
        sesion.Iniciar("no-es-un-jwt", DateTimeOffset.UtcNow.AddMinutes(15));
        var expirada = false;
        sesion.Expirada += (_, _) => expirada = true;

        await Assert.ThrowsAsync<SesionExpiradaException>(() => Cliente(sesion).ListarCasosAsync(CancellationToken.None));

        Assert.True(expirada);
        Assert.False(sesion.Activa);
    }

    [Fact]
    public async Task Con_una_contrasena_temporal_el_cliente_detecta_el_cambio_obligatorio_y_tras_cambiarla_queda_listo()
    {
        var nombre = $"tmp-{Guid.NewGuid():N}"[..16];
        var admin = await servidor.AdminAsync();
        (await admin.PostAsJsonAsync("/usuarios", new CrearUsuarioRequest(nombre, Rol.Analista, "contrasena temporal 2026"), ServidorForense.Json))
            .EnsureSuccessStatusCode();
        var cliente = Cliente();

        Assert.Equal(EstadoDeInicio.DebeCambiarContrasena, await cliente.IniciarSesionAsync(nombre, "contrasena temporal 2026", CancellationToken.None));
        await cliente.CambiarContrasenaAsync("contrasena temporal 2026", "la frase definitiva", CancellationToken.None);

        // Con la contraseña cambiada la sesión ya es completa: listar casos no da 403.
        await cliente.ListarCasosAsync(CancellationToken.None);
        Assert.Equal(EstadoDeInicio.Listo, await Cliente().IniciarSesionAsync(nombre, "la frase definitiva", CancellationToken.None));
    }

    [Fact]
    public async Task El_flujo_del_analista_de_punta_a_punta_con_el_cliente_tipado()
    {
        var cliente = await AnalistaAsync($"flujo-{Guid.NewGuid():N}"[..16]);
        var ct = CancellationToken.None;
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Flujo del escritorio", ct);
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(200_000);
        var enviados = new List<long>();

        var original = await cliente.IngestarAsync(caso.Id, "cámara 1.png", new MemoryStream(bytes), bytes.Length, enviados.Add, ct);
        Assert.Equal(HashSha256.Calcular(bytes).Hex, original.Hash);
        Assert.Equal(bytes.Length, enviados[^1]);
        Assert.Equal(bytes, await cliente.DescargarContenidoAsync(caso.Id, original.Id, ct));

        var aplicada = await cliente.AplicarOperacionAsync(caso.Id, original.Id, "opencv.recorte/v1", new { x = 5, y = 6, ancho = 16, alto = 9 }, null, ct);
        Assert.Equal("""{"alto":9,"ancho":16,"x":5,"y":6}""", aplicada.Parametros);
        Assert.Equal((original.Id, Forensic.Cliente.Api.ViaProcesamiento.AdmisibleEnJuicio), (aplicada.Derivado.PadreId, aplicada.Derivado.Via));
        Assert.Equal(["cámara 1.png", "cámara 1-recorte.png"], (await cliente.ListarEvidenciasAsync(caso.Id, ct)).Select(e => e.NombreOriginal));

        var reproduccion = await cliente.ReproducirAsync(caso.Id, aplicada.Derivado.Id, ct);
        Assert.True(reproduccion.Reproducido);
        Assert.Equal(aplicada.Derivado.Hash, reproduccion.Sha256Obtenido);

        var linaje = await cliente.ObtenerLinajeAsync(caso.Id, aplicada.Derivado.Id, ct);
        Assert.Equal([original.Id, aplicada.Derivado.Id], linaje.Select(e => e.Evidencia.Id));
        Assert.Equal("opencv.recorte/v1", linaje[1].Operacion!.Tipo);
        Assert.True(Assert.Single(linaje[1].Reproducciones).Reproducido);

        var custodia = await cliente.ExportarCustodiaAsync(caso.Id, ct);
        Assert.True(custodia.Verificacion.EsIntegra, custodia.Verificacion.Motivo);
        Assert.Equal(
            ["CasoAbierto", "EvidenciaIngestada", "EvidenciaAccedida", "OperacionAplicada", "OperacionReproducida"],
            custodia.Asientos.Select(a => a.Tipo));
    }

    [Fact]
    public async Task Un_reporte_de_ia_reenviado_por_el_cliente_vuelve_tipado_y_marcado_solo_investigacion()
    {
        var ct = CancellationToken.None;
        var cliente = await AnalistaAsync($"ia-{Guid.NewGuid():N}"[..16]);
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Prueba de IA", ct);
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(2048);
        var original = await cliente.IngestarAsync(caso.Id, "cam1.png", new MemoryStream(bytes), bytes.Length, null, ct);
        // Tal como lo devuelve POST /v1/detectar del worker de IA.
        var reporte = $$$"""
            {"analisis": "ia.deteccion/v1", "parametros": {"umbralConfianzaMilesimas": 500},
             "modelo": {"id": "yolox-s", "version": "0.1.1rc0", "sha256": "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"},
             "entrada": {"sha256": "{{{original.Hash}}}", "ancho": 32, "alto": 32},
             "entorno": {"proveedor": "CPUExecutionProvider"},
             "hallazgos": [{"region": {"x": 1, "y": 2, "ancho": 3, "alto": 4}, "clase": "car", "confianza": 0.8213145732879639,
                            "crudo": {"puntaje": 0.8213145732879639}}]}
            """;

        var analisis = await cliente.RegistrarAnalisisIaAsync(caso.Id, original.Id, reporte, ct);

        Assert.Equal(Forensic.Cliente.Api.ViaProcesamiento.SoloInvestigacion, analisis.Via);
        Assert.Equal(new Forensic.Cliente.Api.ModeloDeIaDto("yolox-s", "0.1.1rc0", "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"), analisis.Modelo);
        var hallazgo = Assert.Single(analisis.Hallazgos);
        Assert.Equal(new Forensic.Cliente.Api.RegionDto(1, 2, 3, 4), hallazgo.Region);
        Assert.Equal(("car", (string?)null, 0.8213145732879639m), (hallazgo.Clase, hallazgo.TextoLeido, hallazgo.Confianza));
        Assert.Equal("""{"puntaje": 0.8213145732879639}""", hallazgo.Crudo);
        Assert.Equal(Forensic.Cliente.Api.ViaProcesamiento.SoloInvestigacion, hallazgo.Via);
        Assert.Equal(analisis, Assert.Single(await cliente.ListarAnalisisIaAsync(caso.Id, original.Id, ct)), new MismoAnalisis());
        Assert.Equal("HallazgosRegistrados", (await cliente.ExportarCustodiaAsync(caso.Id, ct)).Asientos[^1].Tipo);
    }

    [Fact]
    public async Task Un_ocr_reenviado_por_el_cliente_vuelve_con_el_texto_leido()
    {
        var ct = CancellationToken.None;
        var cliente = await AnalistaAsync($"ocr-{Guid.NewGuid():N}"[..16]);
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Prueba de OCR", ct);
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(2048);
        var original = await cliente.IngestarAsync(caso.Id, "patente.png", new MemoryStream(bytes), bytes.Length, null, ct);
        // Tal como lo devuelve POST /v1/ocr del worker de IA.
        var reporte = $$$"""
            {"analisis": "ia.ocr/v1", "parametros": {"umbralConfianzaMilesimas": 500},
             "modelo": {"id": "ppocrv4-en", "version": "PP-OCRv4", "sha256": "3d37f552dfc2354511c40cd5b68793129ce54222aa2279bf4a54dcdee9cf134f"},
             "entrada": {"sha256": "{{{original.Hash}}}", "ancho": 96, "alto": 32},
             "entorno": {"proveedor": "CPUExecutionProvider"},
             "hallazgos": [{"region": {"x": 1, "y": 2, "ancho": 80, "alto": 20}, "textoLeido": "ABC123", "confianza": 0.82,
                            "crudo": {"caracteres": []}}]}
            """;

        var analisis = await cliente.RegistrarAnalisisIaAsync(caso.Id, original.Id, reporte, ct);

        Assert.Equal(("ia.ocr/v1", Forensic.Cliente.Api.ViaProcesamiento.SoloInvestigacion), (analisis.Analisis, analisis.Via));
        var hallazgo = Assert.Single(analisis.Hallazgos);
        Assert.Equal(((string?)null, "ABC123", 0.82m), (hallazgo.Clase, hallazgo.TextoLeido, hallazgo.Confianza));
    }

    [Fact]
    public async Task Una_super_resolucion_registrada_por_el_cliente_crea_un_derivado_solo_investigacion_con_el_modelo_en_el_linaje()
    {
        var ct = CancellationToken.None;
        var cliente = await AnalistaAsync($"sr-{Guid.NewGuid():N}"[..16]);
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Prueba de super-resolución", ct);
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(2048);
        var original = await cliente.IngestarAsync(caso.Id, "cam1.png", new MemoryStream(bytes), bytes.Length, null, ct);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(512)];
        var sha256Png = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToLowerInvariant();
        // Tal como lo manda POST /v1/superresolucion en la cabecera X-Reporte.
        var reporte = $$$"""{"operacion":"ia.superresolucion/v1","parametros":{},"modelo":{"id":"realesrgan-x4plus","version":"v0.1.0","sha256":"1b21f3442cb75a7f76ae99cb7742a84510d14f428a6b2d3d8960440f7235a0bc"},"entrada":{"sha256":"{{{original.Hash}}}","ancho":16,"alto":12},"salida":{"sha256":"{{{sha256Png}}}","ancho":64,"alto":48},"entorno":{"proveedor":"CPUExecutionProvider"}}""";

        var aplicada = await cliente.RegistrarOperacionIaAsync(caso.Id, original.Id, reporte, png, "cam1-x4.png", ct);

        Assert.Equal((sha256Png, "cam1-x4.png", original.Id), (aplicada.Derivado.Hash, aplicada.Derivado.NombreOriginal, aplicada.Derivado.PadreId));
        Assert.Equal(Forensic.Cliente.Api.ViaProcesamiento.SoloInvestigacion, aplicada.Derivado.Via);
        var operacion = (await cliente.ObtenerLinajeAsync(caso.Id, aplicada.Derivado.Id, ct))[^1].Operacion!;
        Assert.Equal(("ia.superresolucion/v1", "realesrgan-x4plus"), (operacion.Tipo, operacion.Modelo!.Id));
    }

    /// <summary>Los records con listas no se comparan por valor: se compara el id y los hallazgos uno por uno.</summary>
    private sealed class MismoAnalisis : IEqualityComparer<Forensic.Cliente.Api.AnalisisIaDto>
    {
        public bool Equals(Forensic.Cliente.Api.AnalisisIaDto? a, Forensic.Cliente.Api.AnalisisIaDto? b) =>
            a is not null && b is not null && a.Id == b.Id && a.Hallazgos.SequenceEqual(b.Hallazgos);

        public int GetHashCode(Forensic.Cliente.Api.AnalisisIaDto a) => a.Id.GetHashCode();
    }
}
