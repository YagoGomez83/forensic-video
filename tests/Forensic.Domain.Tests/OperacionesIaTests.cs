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

/// <summary>
/// Operaciones de IA que producen un derivado (super-resolución): siempre con un modelo de la lista
/// blanca, siempre «sólo investigación», asentadas con su procedencia y nunca reproducibles por el
/// servidor.
/// </summary>
public class OperacionesIaTests
{
    private const string EntornoDeGpu = """{"onnxruntime":"1.31.0","proveedor":"CUDAExecutionProvider"}""";

    private readonly Caso _caso = CasoDeAna();

    private static JsonElement Json(string texto) => JsonDocument.Parse(texto).RootElement;

    private static OperacionDeIa SuperRes() => CatalogoDeIa.InterpretarOperacion(CatalogoDeIa.SuperResolucion, Json("{}"));

    private OperacionForense RegistrarIa(Evidencia entrada, ModeloDeIa? modelo = null, string salida = "sr") =>
        OperacionForense.RegistrarIa(
            _caso, entrada, SuperRes(), modelo ?? RealEsrgan, EntornoDeGpu, HashDe(salida), 4096, Ana, Ahora.AddMinutes(1));

    [Fact]
    public void Una_super_resolucion_registrada_es_solo_investigacion_y_conserva_su_modelo()
    {
        var original = Original(_caso);

        var op = RegistrarIa(original);
        var derivado = original.Derivar(op, "cam1-sr.png");

        Assert.Equal(ViaProcesamiento.SoloInvestigacion, op.Via);
        Assert.Equal(RealEsrgan, op.Modelo);
        Assert.Equal(CatalogoDeIa.SuperResolucion, op.Tipo);
        Assert.Equal("{}", op.Parametros);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, derivado.Via);
        Assert.Throws<ExcepcionDeDominio>(derivado.ExigirAdmisibleEnJuicio);
    }

    [Fact]
    public void Una_operacion_determinista_no_lleva_modelo()
    {
        var op = OperacionForense.Registrar(
            _caso, Original(_caso), Recorte.Crear(0, 0, 4, 4), EntornoDePrueba, HashDe("r"), 64, null, Ana, Ahora);

        Assert.Null(op.Modelo);
        Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, op.Via);
    }

    [Fact]
    public void Un_modelo_fuera_de_la_lista_blanca_no_se_acepta()
    {
        Assert.Throws<ExcepcionDeDominio>(() => RegistrarIa(Original(_caso), RealEsrgan with { Sha256 = HashDe("otros pesos") }));
        Assert.Throws<ExcepcionDeDominio>(() => RegistrarIa(Original(_caso), RealEsrgan with { Version = "v0.2.5" }));
    }

    [Fact]
    public void El_modelo_de_otra_capacidad_no_sirve()
    {
        var yolox = Assert.Single(CatalogoDeIa.Modelos(CatalogoDeIa.Deteccion));

        Assert.Throws<ExcepcionDeDominio>(() => RegistrarIa(Original(_caso), yolox));
    }

    [Fact]
    public void Registrar_solo_acepta_operaciones_deterministas()
    {
        var parametro = typeof(OperacionForense).GetMethod(nameof(OperacionForense.Registrar))!.GetParameters()[2];

        Assert.Equal(typeof(OperacionDeterminista), parametro.ParameterType);
    }

    [Fact]
    public void No_hay_una_tercera_via_fuera_del_dominio()
    {
        // Sólo OperacionDeterminista y OperacionDeIa (del dominio) pueden derivar de DefinicionDeOperacion.
        var constructores = typeof(DefinicionDeOperacion).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.All(constructores, c => Assert.True(c.IsFamilyAndAssembly, $"{c} debería ser private protected."));
    }

    [Fact]
    public void El_asiento_de_una_operacion_de_ia_lleva_la_procedencia_completa()
    {
        var original = Original(_caso);
        var op = RegistrarIa(original);
        var derivado = original.Derivar(op, "cam1-sr.png");

        using var doc = JsonDocument.Parse(op.DetalleDeAsiento(derivado));
        var r = doc.RootElement;

        Assert.Equal("forensic-video/operacion-ia/v1", OperacionForense.FormatoDeDetalleIa);
        Assert.Equal(OperacionForense.FormatoDeDetalleIa, r.GetProperty("formato").GetString());
        Assert.Equal(CatalogoDeIa.SuperResolucion, r.GetProperty("operacion").GetString());
        Assert.Equal("{}", r.GetProperty("parametros").GetRawText());
        Assert.Equal(RealEsrgan.Id, r.GetProperty("modelo").GetProperty("id").GetString());
        Assert.Equal(RealEsrgan.Version, r.GetProperty("modelo").GetProperty("version").GetString());
        Assert.Equal(RealEsrgan.Sha256.Hex, r.GetProperty("modelo").GetProperty("sha256").GetString());
        Assert.Equal(original.Hash.Hex, r.GetProperty("entrada").GetProperty("sha256").GetString());
        Assert.Equal(derivado.Id, r.GetProperty("salida").GetProperty("evidenciaId").GetGuid());
        Assert.Equal(op.HashSalida.Hex, r.GetProperty("salida").GetProperty("sha256").GetString());
        Assert.Equal("declarada-por-workstation", r.GetProperty("procedencia").GetProperty("origen").GetString());
        Assert.Equal(EntornoDeGpu, r.GetProperty("procedencia").GetProperty("entorno").GetRawText());
        Assert.Equal("SoloInvestigacion", r.GetProperty("via").GetString());
    }

    [Fact]
    public void Una_operacion_de_ia_asentada_no_se_lee_como_reproducible()
    {
        var original = Original(_caso);
        var op = RegistrarIa(original);
        var asiento = RegistroDeCustodia.Asentar(
            null, _caso.Id, TipoAsiento.OperacionIaAplicada, Guid.NewGuid(), op.DetalleDeAsiento(original.Derivar(op, "sr.png")),
            Ana, Ahora);

        Assert.Throws<ExcepcionDeDominio>(() => OperacionAsentada.Desde(asiento));
    }

    [Fact]
    public void El_detalle_de_una_operacion_determinista_no_cambia_de_formato()
    {
        var original = Original(_caso);
        var op = OperacionForense.Registrar(
            _caso, original, Recorte.Crear(0, 0, 4, 4), EntornoDePrueba, HashDe("r"), 64, null, Ana, Ahora);

        using var doc = JsonDocument.Parse(op.DetalleDeAsiento(original.Derivar(op, "r.png")));

        Assert.Equal(OperacionForense.FormatoDeDetalle, doc.RootElement.GetProperty("formato").GetString());
        Assert.False(doc.RootElement.TryGetProperty("modelo", out _));
    }

    [Theory]
    [InlineData("""{"escala":4}""")]
    [InlineData("[]")]
    public void La_super_resolucion_no_admite_parametros(string parametros)
    {
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeIa.InterpretarOperacion(CatalogoDeIa.SuperResolucion, Json(parametros)));
    }

    [Fact]
    public void Analisis_y_operaciones_de_ia_no_se_confunden()
    {
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeIa.Interpretar(CatalogoDeIa.SuperResolucion, Json("{}")));
        Assert.Throws<ExcepcionDeDominio>(
            () => CatalogoDeIa.InterpretarOperacion(CatalogoDeIa.Deteccion, Json("""{"umbralConfianzaMilesimas":500}""")));
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeIa.InterpretarOperacion("opencv.recorte/v1", Json("{}")));
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeOperaciones.Interpretar(CatalogoDeIa.SuperResolucion, Json("{}")));
        Assert.Contains(CatalogoDeIa.SuperResolucion, CatalogoDeIa.Tipos);
    }
}
