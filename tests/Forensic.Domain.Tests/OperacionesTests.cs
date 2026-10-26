using System.Text.Json;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Operaciones;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

public class OperacionesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Un_recorte_valido_se_interpreta_con_tipo_versionado_y_parametros_canonicos()
    {
        var op = CatalogoDeOperaciones.Interpretar("opencv.recorte/v1", Json("""{"alto":20,"ancho":30,"x":5,"y":10}"""));

        var recorte = Assert.IsType<Recorte>(op);
        Assert.Equal((5, 10, 30, 20), (recorte.X, recorte.Y, recorte.Ancho, recorte.Alto));
        Assert.False(op.UsaIa);
        Assert.Equal("opencv.recorte/v1", op.Tipo);
        // Canónico: claves ordenadas, sin espacios. Es el string exacto que se asienta y se reenvía al reproducir.
        Assert.Equal("""{"alto":20,"ancho":30,"x":5,"y":10}""", op.ParametrosCanonicos);
    }

    [Fact]
    public void El_orden_y_los_espacios_del_pedido_no_cambian_los_parametros_canonicos()
    {
        var a = CatalogoDeOperaciones.Interpretar("opencv.recorte/v1", Json("""{ "x": 5, "y": 10, "ancho": 30, "alto": 20 }"""));
        var b = CatalogoDeOperaciones.Interpretar("opencv.recorte/v1", Json("""{"alto":20,"y":10,"ancho":30,"x":5}"""));

        Assert.Equal(a.ParametrosCanonicos, b.ParametrosCanonicos);
    }

    [Theory]
    [InlineData("""{"x":-1,"y":0,"ancho":10,"alto":10}""")]
    [InlineData("""{"x":0,"y":0,"ancho":0,"alto":10}""")]
    [InlineData("""{"x":0,"y":0,"ancho":10,"alto":70000}""")]
    [InlineData("""{"x":0.5,"y":0,"ancho":10,"alto":10}""")]
    [InlineData("""{"x":"0","y":0,"ancho":10,"alto":10}""")]
    [InlineData("""{"x":0,"y":0,"ancho":10}""")]
    [InlineData("""{"x":0,"y":0,"ancho":10,"alto":10,"extra":1}""")]
    [InlineData("""[0,0,10,10]""")]
    public void Un_recorte_con_parametros_invalidos_se_rechaza(string parametros)
    {
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeOperaciones.Interpretar("opencv.recorte/v1", Json(parametros)));
    }

    [Theory]
    [InlineData("opencv.recorte")]
    [InlineData("opencv.recorte/v2")]
    [InlineData("realesrgan.x4/v1")]
    [InlineData("")]
    public void Un_tipo_fuera_del_catalogo_se_rechaza(string tipo)
    {
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeOperaciones.Interpretar(tipo, Json("""{"x":0,"y":0,"ancho":1,"alto":1}""")));
    }

    [Fact]
    public void Toda_operacion_del_catalogo_es_determinista()
    {
        Assert.NotEmpty(CatalogoDeOperaciones.Tipos);
        Assert.All(CatalogoDeOperaciones.Tipos, t => Assert.EndsWith("/v1", t));
    }

    [Fact]
    public void Registrar_una_operacion_asienta_tipo_parametros_entorno_y_hashes()
    {
        var caso = CasoDeAna();
        var original = Original(caso);
        var recorte = Recorte.Crear(0, 0, 10, 10);

        var op = OperacionForense.Registrar(
            caso, original, recorte, EntornoDePrueba, HashDe("salida"), 512, "ab12", Ana, Ahora);

        Assert.Equal("opencv.recorte/v1", op.Tipo);
        Assert.Equal(recorte.ParametrosCanonicos, op.Parametros);
        Assert.Equal(EntornoDePrueba, op.Entorno);
        Assert.Equal(original.Hash, op.HashEntrada);
        Assert.Equal(HashDe("salida"), op.HashSalida);
        Assert.Equal("ab12", op.HashPixelesSalida);
        Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, op.Via);
    }

    [Fact]
    public void Sin_entorno_no_hay_operacion_reproducible()
    {
        var caso = CasoDeAna();

        Assert.Throws<ExcepcionDeDominio>(() => OperacionForense.Registrar(
            caso, Original(caso), Recorte.Crear(0, 0, 1, 1), " ", HashDe("s"), 1, null, Ana, Ahora));
    }

    [Fact]
    public void Solo_el_analista_asignado_con_el_caso_abierto_aplica_operaciones()
    {
        var caso = CasoDeAna();
        var original = Original(caso);

        foreach (var actor in new[] { Beto, Supervisora, Auditor, Admin })
            Assert.Throws<AccesoDenegadoException>(() => OperacionForense.Registrar(
                caso, original, Recorte.Crear(0, 0, 1, 1), EntornoDePrueba, HashDe("s"), 1, null, actor, Ahora));
        caso.Cerrar(Supervisora);
        Assert.Throws<AccesoDenegadoException>(() => OperacionForense.Registrar(
            caso, original, Recorte.Crear(0, 0, 1, 1), EntornoDePrueba, HashDe("s"), 1, null, Ana, Ahora));
    }

    [Fact]
    public void Derivar_no_modifica_el_original()
    {
        var caso = CasoDeAna();
        var original = Original(caso);
        var (hash, tamano, via) = (original.Hash, original.TamanoBytes, original.Via);
        var op = OperacionForense.Registrar(caso, original, Recorte.Crear(0, 0, 1, 1), EntornoDePrueba, HashDe("s"), 1, null, Ana, Ahora);

        var derivado = original.Derivar(op, "recorte.png");

        Assert.Equal((hash, tamano, via), (original.Hash, original.TamanoBytes, original.Via));
        Assert.True(original.EsOriginal);
        Assert.Equal(original.Id, derivado.PadreId);
    }

    [Fact]
    public void El_asiento_de_la_operacion_tiene_formato_versionado_y_todo_lo_necesario_para_reproducirla()
    {
        var caso = CasoDeAna();
        var original = Original(caso);
        var op = OperacionForense.Registrar(
            caso, original, Recorte.Crear(5, 10, 30, 20), EntornoDePrueba, HashDe("salida"), 512, "ab12", Ana, Ahora);
        var derivado = original.Derivar(op, "recorte.png");

        var asiento = RegistroDeCustodia.Asentar(
            null, caso.Id, TipoAsiento.OperacionAplicada, derivado.Id, op.DetalleDeAsiento(derivado), Ana, Ahora);
        var leida = OperacionAsentada.Desde(asiento);

        Assert.Equal(OperacionForense.FormatoDeDetalle, leida.Formato);
        Assert.Equal("forensic-video/operacion/v1", leida.Formato);
        Assert.Equal(("opencv.recorte/v1", original.Id, original.Hash), (leida.Tipo, leida.EvidenciaEntradaId, leida.HashEntrada));
        Assert.Equal((derivado.Id, derivado.Hash, 512L, "ab12"), (leida.EvidenciaSalidaId, leida.HashSalida, leida.TamanoSalidaBytes, leida.HashPixelesSalida));
        // Parámetros y entorno, byte a byte como se aplicaron: es lo que se reenvía al reproducir.
        Assert.Equal(op.Parametros, leida.Parametros);
        Assert.Equal(EntornoDePrueba, leida.Entorno);
        Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, leida.Via);
        // Y desde el asiento solo se reconstruye la misma operación del catálogo.
        var reconstruida = CatalogoDeOperaciones.Interpretar(leida.Tipo, JsonDocument.Parse(leida.Parametros).RootElement);
        Assert.Equal(op.Parametros, reconstruida.ParametrosCanonicos);
    }

    [Fact]
    public void Un_asiento_que_no_es_de_operacion_o_de_otro_formato_no_se_lee_como_operacion()
    {
        var caso = CasoDeAna();
        var otro = RegistroDeCustodia.Asentar(null, caso.Id, TipoAsiento.CasoAbierto, null, "{}", Ana, Ahora);
        var formatoFuturo = RegistroDeCustodia.Asentar(
            null, caso.Id, TipoAsiento.OperacionAplicada, Guid.NewGuid(), """{"formato":"forensic-video/operacion/v9"}""", Ana, Ahora);

        Assert.Throws<ExcepcionDeDominio>(() => OperacionAsentada.Desde(otro));
        Assert.Throws<ExcepcionDeDominio>(() => OperacionAsentada.Desde(formatoFuturo));
    }

    [Fact]
    public void El_entorno_debe_ser_un_objeto_json()
    {
        var caso = CasoDeAna();

        Assert.Throws<ExcepcionDeDominio>(() => OperacionForense.Registrar(
            caso, Original(caso), Recorte.Crear(0, 0, 1, 1), "opencv 4.14", HashDe("s"), 1, null, Ana, Ahora));
    }
}
