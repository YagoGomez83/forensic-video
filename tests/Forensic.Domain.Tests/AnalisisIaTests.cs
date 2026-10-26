using System.Reflection;
using System.Text.Json;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;
using Forensic.Domain.Operaciones;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

public class AnalisisIaTests
{
    private const string EntornoDeGpu = """{"onnxruntime":"1.31.0","worker":"0.1.0"}""";
    private const string Crudo = """{"bbox":[10.4,20.7,50.2,30.9],"clase":2,"score":0.8213}""";

    private static readonly ModeloDeIa Yolox =
        new("yolox-s", "0.1.1rc0", HashSha256.Parse("c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"));

    private readonly Caso _caso = CasoDeAna();

    private static JsonElement Json(string texto) => JsonDocument.Parse(texto).RootElement;

    private static DefinicionDeAnalisisIa Deteccion(int umbral = 500) =>
        CatalogoDeIa.Interpretar(CatalogoDeIa.Deteccion, Json($$"""{"umbralConfianzaMilesimas":{{umbral}}}"""));

    private static HallazgoPropuesto Auto(decimal confianza = 0.8213m) =>
        HallazgoPropuesto.Deteccion(RegionDeImagen.Crear(10, 20, 50, 30), "car", confianza, Crudo);

    private AnalisisIa Registrar(
        Evidencia entrada, IReadOnlyList<HallazgoPropuesto>? hallazgos = null, ModeloDeIa? modelo = null,
        HashSha256? hashDeclarado = null, Actor? actor = null, string entorno = EntornoDeGpu) =>
        AnalisisIa.Registrar(
            _caso, entrada, Deteccion(), modelo ?? Yolox, entorno, hashDeclarado ?? entrada.Hash,
            hallazgos ?? [Auto()], actor ?? Ana, Ahora.AddMinutes(5));

    [Fact]
    public void Un_analisis_de_ia_y_todos_sus_hallazgos_son_solo_investigacion()
    {
        var analisis = Registrar(Original(_caso), [Auto(), Auto(0.51m)]);

        Assert.Equal(ViaProcesamiento.SoloInvestigacion, analisis.Via);
        Assert.Equal(2, analisis.Hallazgos.Count);
        Assert.All(analisis.Hallazgos, h => Assert.Equal(ViaProcesamiento.SoloInvestigacion, h.Via));
        Assert.All(analisis.Hallazgos, h => Assert.Equal(analisis.Id, h.AnalisisId));
    }

    [Theory]
    [InlineData(typeof(AnalisisIa))]
    [InlineData(typeof(Hallazgo))]
    public void La_via_de_lo_producido_por_ia_no_se_puede_asignar(Type tipo)
    {
        var via = tipo.GetProperty(nameof(AnalisisIa.Via))!;

        // Sin setter de ningún tipo (ni privado): no hay camino, ni por EF Core, para volverlo admisible.
        Assert.Null(via.SetMethod);
        Assert.DoesNotContain(
            tipo.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters()),
            p => p.ParameterType == typeof(ViaProcesamiento));
    }

    [Fact]
    public void El_hallazgo_conserva_la_confianza_y_el_dato_crudo_del_modelo_tal_cual()
    {
        var hallazgo = Registrar(Original(_caso)).Hallazgos.Single();

        Assert.Equal(0.8213m, hallazgo.Confianza);
        Assert.Equal(Crudo, hallazgo.DatoCrudo);
        Assert.Equal("car", hallazgo.Clase);
        Assert.Null(hallazgo.TextoLeido);
        Assert.Equal(RegionDeImagen.Crear(10, 20, 50, 30), hallazgo.Region);
    }

    [Fact]
    public void Un_hallazgo_de_texto_es_lo_que_el_modelo_leyo_no_un_dato_verificado()
    {
        var leido = HallazgoPropuesto.Texto(RegionDeImagen.Crear(0, 0, 80, 20), "ABC123", 0.82m, """{"texto":"ABC123"}""");

        var hallazgo = Registrar(Original(_caso), [leido]).Hallazgos.Single();

        Assert.Equal("ABC123", hallazgo.TextoLeido);
        Assert.Null(hallazgo.Clase);
        Assert.Equal(0.82m, hallazgo.Confianza);
    }

    [Fact]
    public void Un_analisis_sin_hallazgos_se_registra_igual()
    {
        var analisis = Registrar(Original(_caso), []);

        Assert.Empty(analisis.Hallazgos);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, analisis.Via);
    }

    [Fact]
    public void La_procedencia_queda_registrada()
    {
        var original = Original(_caso);

        var analisis = Registrar(original);

        Assert.Equal(CatalogoDeIa.Deteccion, analisis.Tipo);
        Assert.Equal("""{"umbralConfianzaMilesimas":500}""", analisis.Parametros);
        Assert.Equal(Yolox, analisis.Modelo);
        Assert.Equal(EntornoDeGpu, analisis.Entorno);
        Assert.Equal(original.Id, analisis.EvidenciaId);
        Assert.Equal(original.Hash, analisis.HashEntrada);
        Assert.Equal("ana", analisis.Actor);
    }

    [Fact]
    public void Si_el_worker_analizo_otro_contenido_no_se_registra()
    {
        var ex = Assert.Throws<ExcepcionDeDominio>(() => Registrar(Original(_caso), hashDeclarado: HashDe("otra cosa")));

        Assert.Contains("otro contenido", ex.Message);
    }

    [Fact]
    public void Un_modelo_fuera_de_la_lista_blanca_no_se_acepta()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Registrar(Original(_caso), modelo: Yolox with { Id = "yolo-pirata" }));
    }

    [Fact]
    public void Un_modelo_con_pesos_distintos_a_los_permitidos_no_se_acepta()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Registrar(Original(_caso), modelo: Yolox with { Sha256 = HashDe("otros pesos") }));
    }

    [Fact]
    public void Sin_entorno_declarado_no_hay_procedencia()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Registrar(Original(_caso), entorno: "no es json"));
    }

    [Fact]
    public void Solo_un_analista_asignado_puede_registrar_un_analisis()
    {
        Assert.Throws<AccesoDenegadoException>(() => Registrar(Original(_caso), actor: Beto));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void La_confianza_esta_entre_cero_y_uno(decimal confianza)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Auto(confianza));
    }

    [Theory]
    [InlineData(-1, 0, 10, 10)]
    [InlineData(0, -1, 10, 10)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(0, 0, 10, 0)]
    public void La_region_son_pixeles_enteros_no_negativos_y_no_vacia(int x, int y, int ancho, int alto)
    {
        Assert.Throws<ExcepcionDeDominio>(() => RegionDeImagen.Crear(x, y, ancho, alto));
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("no es json")]
    [InlineData("")]
    public void El_dato_crudo_es_un_objeto_json(string crudo)
    {
        Assert.Throws<ExcepcionDeDominio>(
            () => HallazgoPropuesto.Deteccion(RegionDeImagen.Crear(0, 0, 1, 1), "car", 0.5m, crudo));
    }

    [Fact]
    public void El_reporte_viene_de_la_workstation_y_tiene_tope_de_hallazgos()
    {
        var demasiados = Enumerable.Repeat(Auto(), AnalisisIa.MaximoHallazgos + 1).ToList();

        Assert.Throws<ExcepcionDeDominio>(() => Registrar(Original(_caso), demasiados));
        Assert.Equal(AnalisisIa.MaximoHallazgos, Registrar(Original(_caso), demasiados[1..]).Hallazgos.Count);
    }

    [Fact]
    public void El_dato_crudo_tiene_tope_de_tamano()
    {
        var region = RegionDeImagen.Crear(0, 0, 1, 1);
        var largo = $$"""{"x":"{{new string('a', HallazgoPropuesto.LongitudMaximaCrudo)}}"}""";

        Assert.Throws<ExcepcionDeDominio>(() => HallazgoPropuesto.Deteccion(region, "car", 0.5m, largo));
    }

    [Fact]
    public void El_entorno_declarado_tiene_tope_de_tamano()
    {
        var largo = $$"""{"x":"{{new string('a', AnalisisIa.LongitudMaximaEntorno)}}"}""";

        Assert.Throws<ExcepcionDeDominio>(() => Registrar(Original(_caso), entorno: largo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("con\nsalto")]
    public void La_clase_y_el_texto_se_validan(string valor)
    {
        var region = RegionDeImagen.Crear(0, 0, 1, 1);
        Assert.Throws<ExcepcionDeDominio>(() => HallazgoPropuesto.Deteccion(region, valor, 0.5m, "{}"));
        Assert.Throws<ExcepcionDeDominio>(() => HallazgoPropuesto.Texto(region, valor, 0.5m, "{}"));
    }

    [Theory]
    [InlineData("""{"umbralConfianzaMilesimas":0}""")]
    [InlineData("""{"umbralConfianzaMilesimas":1000}""")]
    [InlineData("""{"umbralConfianzaMilesimas":0.5}""")]
    [InlineData("""{"umbralConfianzaMilesimas":500,"otro":1}""")]
    [InlineData("""{}""")]
    public void Los_parametros_de_la_deteccion_se_validan(string parametros)
    {
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeIa.Interpretar(CatalogoDeIa.Deteccion, Json(parametros)));
    }

    [Fact]
    public void Los_catalogos_no_se_cruzan()
    {
        // Ningún tipo de IA entra por el catálogo determinista, ni uno determinista por el de IA.
        Assert.Throws<ExcepcionDeDominio>(
            () => CatalogoDeOperaciones.Interpretar(CatalogoDeIa.Deteccion, Json("""{"umbralConfianzaMilesimas":500}""")));
        Assert.Throws<ExcepcionDeDominio>(
            () => CatalogoDeIa.Interpretar("opencv.recorte/v1", Json("""{"x":0,"y":0,"ancho":1,"alto":1}""")));
        Assert.Empty(CatalogoDeIa.Tipos.Intersect(CatalogoDeOperaciones.Tipos));
        Assert.All(CatalogoDeIa.Tipos, t => Assert.StartsWith("ia.", t));
    }

    [Fact]
    public void Analizar_un_derivado_de_ia_tambien_es_solo_investigacion()
    {
        var original = Original(_caso);
        var superRes = OperacionForense.RegistrarIa(
            _caso, original, SuperRes(), RealEsrgan, EntornoDeGpu, HashDe("sr"), 4096, Ana, Ahora.AddMinutes(1));
        var derivado = original.Derivar(superRes, "sr.png");

        var analisis = Registrar(derivado);

        Assert.Equal(ViaProcesamiento.SoloInvestigacion, derivado.Via);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, analisis.Via);
    }

    [Fact]
    public void Una_operacion_de_ia_siempre_saca_al_derivado_de_la_via_judicial()
    {
        var original = Original(_caso);
        var op = OperacionForense.RegistrarIa(
            _caso, original, SuperRes(), RealEsrgan, EntornoDeGpu, HashDe("sr"), 4096, Ana, Ahora.AddMinutes(1));

        var derivado = original.Derivar(op, "sr.png");
        var nieto = derivado.Derivar(
            OperacionForense.Registrar(_caso, derivado, Recorte.Crear(0, 0, 4, 4), EntornoDePrueba, HashDe("sr+recorte"), 64,
                null, Ana, Ahora.AddMinutes(2)),
            "sr-recorte.png");

        Assert.True(SuperRes().UsaIa);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, op.Via);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, nieto.Via);
        Assert.Throws<ExcepcionDeDominio>(nieto.ExigirAdmisibleEnJuicio);
    }

    [Fact]
    public void Una_operacion_de_ia_no_puede_declararse_sin_ia()
    {
        // UsaIa está sellado en OperacionDeIa: una subclase no puede sobrescribirlo.
        var usaIa = typeof(OperacionDeIa).GetProperty(nameof(OperacionDeIa.UsaIa))!.GetMethod!;

        Assert.True(usaIa.IsFinal);
    }

    [Fact]
    public void El_asiento_lleva_la_procedencia_completa_y_cada_hallazgo_con_su_dato_crudo_exacto()
    {
        var original = Original(_caso);
        var leido = HallazgoPropuesto.Texto(RegionDeImagen.Crear(1, 2, 3, 4), "ABC123", 0.82m, """{"texto": "ABC123"}""");
        var analisis = Registrar(original, [Auto(), leido]);

        var detalle = analisis.DetalleDeAsiento();

        using var doc = JsonDocument.Parse(detalle);
        var r = doc.RootElement;
        Assert.Equal(AnalisisIa.FormatoDeDetalle, r.GetProperty("formato").GetString());
        Assert.Equal("forensic-video/hallazgos-ia/v1", AnalisisIa.FormatoDeDetalle);
        Assert.Equal(analisis.Id, r.GetProperty("analisisId").GetGuid());
        Assert.Equal(CatalogoDeIa.Deteccion, r.GetProperty("analisis").GetString());
        Assert.Equal("""{"umbralConfianzaMilesimas":500}""", r.GetProperty("parametros").GetRawText());
        var modelo = r.GetProperty("modelo");
        Assert.Equal("yolox-s", modelo.GetProperty("id").GetString());
        Assert.Equal("0.1.1rc0", modelo.GetProperty("version").GetString());
        Assert.Equal(Yolox.Sha256.Hex, modelo.GetProperty("sha256").GetString());
        Assert.Equal(original.Id, r.GetProperty("entrada").GetProperty("evidenciaId").GetGuid());
        Assert.Equal(original.Hash.Hex, r.GetProperty("entrada").GetProperty("sha256").GetString());
        // Lo que el servidor no puede verificar queda dicho como tal.
        var procedencia = r.GetProperty("procedencia");
        Assert.Equal("declarada-por-workstation", procedencia.GetProperty("origen").GetString());
        Assert.Equal(EntornoDeGpu, procedencia.GetProperty("entorno").GetRawText());
        Assert.Equal("SoloInvestigacion", r.GetProperty("via").GetString());

        var hallazgos = r.GetProperty("hallazgos").EnumerateArray().ToList();
        Assert.Equal(2, hallazgos.Count);
        Assert.Equal("car", hallazgos[0].GetProperty("clase").GetString());
        Assert.False(hallazgos[0].TryGetProperty("textoLeido", out _));
        Assert.Equal("0.8213", hallazgos[0].GetProperty("confianza").GetRawText());
        Assert.Equal(Crudo, hallazgos[0].GetProperty("crudo").GetRawText());
        Assert.Equal("ABC123", hallazgos[1].GetProperty("textoLeido").GetString());
        Assert.False(hallazgos[1].TryGetProperty("clase", out _));
        Assert.Equal("""{"texto": "ABC123"}""", hallazgos[1].GetProperty("crudo").GetRawText());
        var region = hallazgos[1].GetProperty("region");
        Assert.Equal((1, 2, 3, 4), (region.GetProperty("x").GetInt32(), region.GetProperty("y").GetInt32(),
            region.GetProperty("ancho").GetInt32(), region.GetProperty("alto").GetInt32()));
    }

    [Fact]
    public void Un_analisis_sin_hallazgos_tambien_se_asienta()
    {
        var detalle = Registrar(Original(_caso), []).DetalleDeAsiento();

        using var doc = JsonDocument.Parse(detalle);
        Assert.Equal(0, doc.RootElement.GetProperty("hallazgos").GetArrayLength());
    }

    [Fact]
    public void El_asiento_de_hallazgos_se_encadena_en_la_custodia_del_caso()
    {
        var original = Original(_caso);
        var ingesta = RegistroDeCustodia.Asentar(
            null, _caso.Id, TipoAsiento.EvidenciaIngestada, original.Id, "{}", Ana, Ahora);
        var analisis = Registrar(original);

        var asiento = RegistroDeCustodia.Asentar(
            ingesta, _caso.Id, TipoAsiento.HallazgosRegistrados, original.Id, analisis.DetalleDeAsiento(), Ana,
            analisis.RegistradoEn);

        Assert.Equal(ingesta.Hash, asiento.HashAnterior);
        Assert.True(RegistroDeCustodia.Verificar(_caso.Id, [ingesta, asiento]).EsIntegra);
    }

    private static OperacionDeIa SuperRes() =>
        CatalogoDeIa.InterpretarOperacion(CatalogoDeIa.SuperResolucion, Json("{}"));
}
