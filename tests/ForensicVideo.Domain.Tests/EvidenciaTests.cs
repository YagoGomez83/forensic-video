using ForensicVideo.Domain.Casos;
using ForensicVideo.Domain.Comun;
using ForensicVideo.Domain.Evidencias;
using ForensicVideo.Domain.Operaciones;
using static ForensicVideo.Domain.Tests.Fixtures;

namespace ForensicVideo.Domain.Tests;

public class EvidenciaTests
{
    private readonly Caso _caso = CasoDeAna();

    private OperacionForense Operacion(Evidencia entrada, bool usaIa, string salida) =>
        OperacionForense.Registrar(
            _caso, entrada, usaIa ? "realesrgan.x4" : "opencv.denoise.nlmeans", "1.0.0", "{}", usaIa,
            HashDe(salida), 2048, Ana, Ahora.AddMinutes(1));

    [Fact]
    public void Un_original_ingresa_admisible_en_juicio_y_sin_padre()
    {
        var e = Original(_caso);

        Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, e.Via);
        Assert.True(e.EsOriginal);
    }

    [Fact]
    public void Sin_hash_no_hay_ingesta()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Evidencia.Ingestar(_caso, default, 10, "x.mp4", Ana, Ahora));
    }

    [Fact]
    public void Una_evidencia_vacia_no_se_ingesta()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Evidencia.Ingestar(_caso, HashDe(""), 0, "x.mp4", Ana, Ahora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("con\nsalto.mp4")]
    public void El_nombre_original_se_valida(string nombre)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Evidencia.Ingestar(_caso, HashDe("v"), 10, nombre, Ana, Ahora));
    }

    [Fact]
    public void Solo_un_analista_asignado_puede_ingestar()
    {
        Assert.Throws<AccesoDenegadoException>(() => Evidencia.Ingestar(_caso, HashDe("v"), 10, "x.mp4", Beto, Ahora));
        Assert.Throws<AccesoDenegadoException>(() => Evidencia.Ingestar(_caso, HashDe("v"), 10, "x.mp4", Auditor, Ahora));
    }

    [Fact]
    public void Un_derivado_determinista_sigue_siendo_admisible_y_referencia_a_su_padre()
    {
        var original = Original(_caso);
        var op = Operacion(original, usaIa: false, "denoised");

        var derivado = original.Derivar(op, "cam1-denoise.png");

        Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, derivado.Via);
        Assert.Equal(original.Id, derivado.PadreId);
        Assert.Equal(op.Id, derivado.OperacionOrigenId);
        Assert.Equal(op.HashSalida, derivado.Hash);
        Assert.NotEqual(original.Hash, derivado.Hash);
    }

    [Fact]
    public void La_marca_solo_investigacion_se_hereda_a_todo_descendiente()
    {
        var original = Original(_caso);
        var conIa = original.Derivar(Operacion(original, usaIa: true, "superres"), "sr.png");
        var nieto = conIa.Derivar(Operacion(conIa, usaIa: false, "superres+denoise"), "sr-dn.png");

        Assert.Equal(ViaProcesamiento.SoloInvestigacion, conIa.Via);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, nieto.Via);
        Assert.Throws<ExcepcionDeDominio>(nieto.ExigirAdmisibleEnJuicio);
    }

    [Fact]
    public void No_se_puede_derivar_con_una_operacion_aplicada_a_otra_evidencia()
    {
        var a = Original(_caso);
        var opSobreA = Operacion(a, usaIa: false, "salida");
        var b = Evidencia.Ingestar(_caso, HashDe("otro"), 10, "cam2.mp4", Ana, Ahora);

        Assert.Throws<ExcepcionDeDominio>(() => b.Derivar(opSobreA, "x.png"));
    }

    [Fact]
    public void Una_operacion_que_no_cambia_el_contenido_no_produce_derivado()
    {
        var original = Original(_caso);
        var op = Operacion(original, usaIa: false, "video");

        Assert.Throws<ExcepcionDeDominio>(() => original.Derivar(op, "igual.mp4"));
    }
}
