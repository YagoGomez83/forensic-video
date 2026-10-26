using System.Text.Json;
using Forensic.Domain.Comun;
using Forensic.Domain.Operaciones;

namespace Forensic.Domain.Ia;

/// <summary>Un análisis de IA a registrar: tipo versionado y parámetros en JSON canónico.</summary>
public sealed record DefinicionDeAnalisisIa(string Tipo, string ParametrosCanonicos);

/// <summary>
/// El carril de IA, separado de <see cref="CatalogoDeOperaciones"/>: ningún tipo <c>ia.*</c> entra por
/// la vía determinista ni uno determinista por acá. Hay análisis (producen hallazgos) y operaciones
/// (producen un derivado), y no se confunden. Cada tipo tiene su lista blanca de modelos (id, versión y
/// hash de los pesos, espejo de <c>app/modelos.json</c> del worker de IA): el servidor no puede
/// verificar la ejecución, pero sí rechazar un modelo que no conoce.
/// </summary>
public static class CatalogoDeIa
{
    public const string Deteccion = "ia.deteccion/v1";
    public const string SuperResolucion = SuperResolucionX4.TipoVersionado;

    public const int UmbralMinimoMilesimas = 1;
    public const int UmbralMaximoMilesimas = 999;

    private static readonly ModeloDeIa Yolox =
        new("yolox-s", "0.1.1rc0", HashSha256.Parse("c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"));

    private static readonly ModeloDeIa RealEsrgan =
        new("realesrgan-x4plus", "v0.1.0", HashSha256.Parse("1b21f3442cb75a7f76ae99cb7742a84510d14f428a6b2d3d8960440f7235a0bc"));

    private static readonly Dictionary<string, ModeloDeIa[]> ModelosPermitidos = new()
    {
        [Deteccion] = [Yolox],
        [SuperResolucion] = [RealEsrgan],
    };

    private static readonly Dictionary<string, Func<LectorDeParametros, OperacionDeIa>> Operaciones = new()
    {
        [SuperResolucion] = SuperResolucionX4.Desde,
    };

    public static IReadOnlyCollection<string> Tipos => ModelosPermitidos.Keys;

    public static IReadOnlyList<ModeloDeIa> Modelos(string tipo) => ExigirTipo(tipo);

    /// <summary>Un análisis (produce hallazgos, no un derivado).</summary>
    public static DefinicionDeAnalisisIa Interpretar(string tipo, JsonElement parametros)
    {
        if (tipo != Deteccion)
            throw new ExcepcionDeDominio($"'{tipo}' no es un análisis de IA del catálogo.");
        var lector = new LectorDeParametros(tipo, parametros);
        var umbral = lector.Entero("umbralConfianzaMilesimas");
        lector.ExigirSinSobrantes();
        if (umbral is < UmbralMinimoMilesimas or > UmbralMaximoMilesimas)
            throw new ExcepcionDeDominio(
                $"El umbral de confianza va de {UmbralMinimoMilesimas} a {UmbralMaximoMilesimas} milésimas.");
        return new DefinicionDeAnalisisIa(tipo, Json.Canonico(("umbralConfianzaMilesimas", umbral)));
    }

    /// <summary>Una operación de IA (produce un derivado «sólo investigación»).</summary>
    public static OperacionDeIa InterpretarOperacion(string tipo, JsonElement parametros)
    {
        if (tipo is null || !Operaciones.TryGetValue(tipo, out var crear))
            throw new ExcepcionDeDominio($"'{tipo}' no es una operación de IA del catálogo.");
        var lector = new LectorDeParametros(tipo, parametros);
        var operacion = crear(lector);
        lector.ExigirSinSobrantes();
        return operacion;
    }

    public static void ExigirModeloPermitido(string tipo, ModeloDeIa modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        if (!ExigirTipo(tipo).Contains(modelo))
            throw new ExcepcionDeDominio(
                $"El modelo '{modelo.Id}' {modelo.Version} (pesos {modelo.Sha256}) no está permitido para '{tipo}'.");
    }

    private static ModeloDeIa[] ExigirTipo(string tipo)
    {
        if (tipo is null || !ModelosPermitidos.TryGetValue(tipo, out var modelos))
            throw new ExcepcionDeDominio($"'{tipo}' no está en el catálogo de IA.");
        return modelos;
    }
}
