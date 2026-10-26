using System.Text;
using System.Text.Json;

namespace Forensic.Domain.Comun;

internal static class Json
{
    public static bool EsObjeto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(texto);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>JSON con las claves ordenadas y sin espacios. Sólo enteros y strings: ningún float ambiguo.</summary>
    public static string Canonico(params (string Clave, object Valor)[] campos)
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
