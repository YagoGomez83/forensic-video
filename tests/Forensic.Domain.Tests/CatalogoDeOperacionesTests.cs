using System.Text.Json;
using Forensic.Domain.Comun;
using Forensic.Domain.Operaciones;

namespace Forensic.Domain.Tests;

/// <summary>Validación de parámetros de cada operación del catálogo. Una fila por caso.</summary>
public class CatalogoDeOperacionesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    public static TheoryData<string, string, string> Validas => new()
    {
        {
            "opencv.perspectiva/v1",
            """{"x1":0,"y1":0,"x2":99,"y2":3,"x3":97,"y3":60,"x4":2,"y4":58,"ancho":100,"alto":60,"interpolacion":"lineal"}""",
            """{"alto":60,"ancho":100,"interpolacion":"lineal","x1":0,"x2":99,"x3":97,"x4":2,"y1":0,"y2":3,"y3":60,"y4":58}"""
        },
        { "opencv.rotacion90/v1", """{"giros":3}""", """{"giros":3}""" },
        { "opencv.rotacion/v1", """{"interpolacion":"cubica","centesimasDeGrado":-1250}""", """{"centesimasDeGrado":-1250,"interpolacion":"cubica"}""" },
        { "opencv.denoise.nlmeans/v1", """{"h":10,"hColor":10,"plantilla":7,"busqueda":21}""", """{"busqueda":21,"h":10,"hColor":10,"plantilla":7}""" },
        { "opencv.denoise.mediana/v1", """{"ventana":5}""", """{"ventana":5}""" },
        { "opencv.realce.unsharp/v1", """{"sigmaCentesimas":150,"cantidadCentesimas":80}""", """{"cantidadCentesimas":80,"sigmaCentesimas":150}""" },
        { "opencv.realce.clahe/v1", """{"limiteDecimas":20,"cuadricula":8}""", """{"cuadricula":8,"limiteDecimas":20}""" },
        { "opencv.tonal.brillo-contraste/v1", """{"contrasteCentesimas":120,"brillo":-15}""", """{"brillo":-15,"contrasteCentesimas":120}""" },
        { "opencv.tonal.gamma/v1", """{"gammaMilesimas":2200}""", """{"gammaMilesimas":2200}""" },
        { "opencv.tonal.ecualizacion/v1", """{}""", """{}""" },
    };

    public static TheoryData<string, string> Invalidas => new()
    {
        // Perspectiva: interpolación fuera de la lista, tamaño de salida inválido o enorme, falta un punto.
        { "opencv.perspectiva/v1", """{"x1":0,"y1":0,"x2":9,"y2":0,"x3":9,"y3":9,"x4":0,"y4":9,"ancho":10,"alto":10,"interpolacion":"lanczos"}""" },
        { "opencv.perspectiva/v1", """{"x1":0,"y1":0,"x2":9,"y2":0,"x3":9,"y3":9,"x4":0,"y4":9,"ancho":0,"alto":10,"interpolacion":"lineal"}""" },
        { "opencv.perspectiva/v1", """{"x1":0,"y1":0,"x2":9,"y2":0,"x3":9,"y3":9,"x4":0,"y4":9,"ancho":20000,"alto":20000,"interpolacion":"lineal"}""" },
        { "opencv.perspectiva/v1", """{"x1":0,"y1":0,"x2":9,"y2":0,"x3":9,"y3":9,"x4":0,"ancho":10,"alto":10,"interpolacion":"lineal"}""" },
        // Rotación: giros fuera de 1..3, ángulo nulo o fuera de ±360°.
        { "opencv.rotacion90/v1", """{"giros":0}""" },
        { "opencv.rotacion90/v1", """{"giros":4}""" },
        { "opencv.rotacion/v1", """{"centesimasDeGrado":0,"interpolacion":"lineal"}""" },
        { "opencv.rotacion/v1", """{"centesimasDeGrado":36001,"interpolacion":"lineal"}""" },
        { "opencv.rotacion/v1", """{"centesimasDeGrado":90}""" },
        // Denoise: ventanas pares, búsqueda menor que la plantilla, fuerza fuera de rango.
        { "opencv.denoise.nlmeans/v1", """{"h":10,"hColor":10,"plantilla":6,"busqueda":21}""" },
        { "opencv.denoise.nlmeans/v1", """{"h":10,"hColor":10,"plantilla":9,"busqueda":7}""" },
        { "opencv.denoise.nlmeans/v1", """{"h":0,"hColor":10,"plantilla":7,"busqueda":21}""" },
        { "opencv.denoise.nlmeans/v1", """{"h":101,"hColor":10,"plantilla":7,"busqueda":21}""" },
        { "opencv.denoise.mediana/v1", """{"ventana":4}""" },
        { "opencv.denoise.mediana/v1", """{"ventana":17}""" },
        // Realce: sigma o cantidad fuera de rango, cuadrícula o límite nulos.
        { "opencv.realce.unsharp/v1", """{"sigmaCentesimas":5,"cantidadCentesimas":80}""" },
        { "opencv.realce.unsharp/v1", """{"sigmaCentesimas":150,"cantidadCentesimas":0}""" },
        { "opencv.realce.clahe/v1", """{"limiteDecimas":0,"cuadricula":8}""" },
        { "opencv.realce.clahe/v1", """{"limiteDecimas":20,"cuadricula":65}""" },
        // Tonal: contraste nulo, brillo fuera de rango, gamma fuera de rango, ecualización con parámetros.
        { "opencv.tonal.brillo-contraste/v1", """{"contrasteCentesimas":0,"brillo":0}""" },
        { "opencv.tonal.brillo-contraste/v1", """{"contrasteCentesimas":100,"brillo":70000}""" },
        { "opencv.tonal.gamma/v1", """{"gammaMilesimas":99}""" },
        { "opencv.tonal.gamma/v1", """{"gammaMilesimas":10001}""" },
        { "opencv.tonal.ecualizacion/v1", """{"canal":"y"}""" },
    };

    [Theory]
    [MemberData(nameof(Validas))]
    public void Los_parametros_validos_se_canonicalizan(string tipo, string parametros, string canonicos)
    {
        var op = CatalogoDeOperaciones.Interpretar(tipo, Json(parametros));

        Assert.Equal(tipo, op.Tipo);
        Assert.False(op.UsaIa);
        Assert.Equal(canonicos, op.ParametrosCanonicos);
        // Idempotente: interpretar los canónicos da los mismos canónicos.
        Assert.Equal(canonicos, CatalogoDeOperaciones.Interpretar(tipo, Json(canonicos)).ParametrosCanonicos);
    }

    [Theory]
    [MemberData(nameof(Invalidas))]
    public void Los_parametros_invalidos_se_rechazan(string tipo, string parametros)
    {
        Assert.Throws<ExcepcionDeDominio>(() => CatalogoDeOperaciones.Interpretar(tipo, Json(parametros)));
    }
}
