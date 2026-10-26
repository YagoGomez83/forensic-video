using System.Text;
using System.Text.Json;
using Forensic.Domain.Comun;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Qué se le aplica a una evidencia: un tipo versionado (<c>opencv.recorte/v1</c>) y sus parámetros
/// en JSON canónico. Ese string exacto se asienta y es el que se reenvía al reproducir: nunca se
/// re-serializa, así que no hay diferencias de formato entre el servidor y el worker.
/// </summary>
public abstract class DefinicionDeOperacion
{
    public abstract string Tipo { get; }

    /// <summary>Si es cierto, el derivado sale de la vía judicial (doble vía).</summary>
    public abstract bool UsaIa { get; }

    public abstract string ParametrosCanonicos { get; }
}

/// <summary>Operación clásica y determinista (OpenCV): el derivado sigue admisible en juicio.</summary>
public abstract class OperacionDeterminista : DefinicionDeOperacion
{
    public sealed override bool UsaIa => false;

    /// <summary>JSON con las claves ordenadas y sin espacios. Sólo enteros y strings: ningún float ambiguo.</summary>
    protected static string Canonico(params (string Clave, object Valor)[] campos)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            foreach (var (clave, valor) in campos.OrderBy(c => c.Clave, StringComparer.Ordinal))
            {
                switch (valor)
                {
                    case int i:
                        w.WriteNumber(clave, i);
                        break;
                    case string s:
                        w.WriteString(clave, s);
                        break;
                    default:
                        throw new InvalidOperationException($"Tipo de parámetro no canónico: {valor.GetType()}.");
                }
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>
/// Las operaciones que el sistema sabe aplicar. En la Fase 2 son todas deterministas: lo que no
/// está acá no se puede aplicar, y nada de IA entra por esta vía.
/// </summary>
public static class CatalogoDeOperaciones
{
    private static readonly Dictionary<string, Func<LectorDeParametros, OperacionDeterminista>> Operaciones = new()
    {
        [Recorte.TipoVersionado] = Recorte.Desde,
        [Perspectiva.TipoVersionado] = Perspectiva.Desde,
        [Rotacion90.TipoVersionado] = Rotacion90.Desde,
        [Rotacion.TipoVersionado] = Rotacion.Desde,
        [DenoiseNlMeans.TipoVersionado] = DenoiseNlMeans.Desde,
        [DenoiseMediana.TipoVersionado] = DenoiseMediana.Desde,
        [RealceUnsharp.TipoVersionado] = RealceUnsharp.Desde,
        [RealceClahe.TipoVersionado] = RealceClahe.Desde,
    };

    public static IReadOnlyCollection<string> Tipos => Operaciones.Keys;

    public static OperacionDeterminista Interpretar(string tipo, JsonElement parametros)
    {
        if (tipo is null || !Operaciones.TryGetValue(tipo, out var crear))
            throw new ExcepcionDeDominio($"La operación '{tipo}' no está en el catálogo.");
        var lector = new LectorDeParametros(tipo, parametros);
        var operacion = crear(lector);
        lector.ExigirSinSobrantes();
        return operacion;
    }
}

/// <summary>Lee parámetros exigiendo tipos exactos y rechazando campos que no corresponden.</summary>
public sealed class LectorDeParametros
{
    private readonly string _tipo;
    private readonly JsonElement _parametros;
    private readonly HashSet<string> _leidos = new(StringComparer.Ordinal);

    internal LectorDeParametros(string tipo, JsonElement parametros)
    {
        if (parametros.ValueKind != JsonValueKind.Object)
            throw new ExcepcionDeDominio($"Los parámetros de '{tipo}' deben ser un objeto JSON.");
        _tipo = tipo;
        _parametros = parametros;
    }

    public int Entero(string nombre)
    {
        _leidos.Add(nombre);
        if (!_parametros.TryGetProperty(nombre, out var valor))
            throw new ExcepcionDeDominio($"'{_tipo}' requiere el parámetro '{nombre}'.");
        if (valor.ValueKind != JsonValueKind.Number || !valor.TryGetInt32(out var entero))
            throw new ExcepcionDeDominio($"El parámetro '{nombre}' de '{_tipo}' debe ser un entero.");
        return entero;
    }

    /// <summary>Un string de una lista cerrada, p. ej. el modo de interpolación.</summary>
    public string Opcion(string nombre, params string[] opciones)
    {
        _leidos.Add(nombre);
        if (!_parametros.TryGetProperty(nombre, out var valor))
            throw new ExcepcionDeDominio($"'{_tipo}' requiere el parámetro '{nombre}'.");
        if (valor.ValueKind != JsonValueKind.String || !opciones.Contains(valor.GetString(), StringComparer.Ordinal))
            throw new ExcepcionDeDominio($"El parámetro '{nombre}' de '{_tipo}' debe ser uno de: {string.Join(", ", opciones)}.");
        return valor.GetString()!;
    }

    internal void ExigirSinSobrantes()
    {
        var sobrantes = _parametros.EnumerateObject().Select(p => p.Name).Where(n => !_leidos.Contains(n)).ToList();
        if (sobrantes.Count > 0)
            throw new ExcepcionDeDominio($"'{_tipo}' no admite los parámetros: {string.Join(", ", sobrantes)}.");
    }
}
