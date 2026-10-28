using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Informes;

namespace Forensic.Application.Informes;

/// <summary>Una evidencia tal como va al bundle: embebida en su ruta, o aparte (sólo su hash).</summary>
internal sealed record EvidenciaDelBundle(Evidencia Evidencia, long AsientoDeOrigen, string? Ruta)
{
    public bool Embebida => Ruta is not null;
}

/// <summary>
/// Escritor del bundle <c>forensic-video/bundle/v1</c>, exactamente como lo especifica
/// docs/formato-bundle-v1.md. Lo verifica, sin compartir código, tools/verificador.
/// </summary>
internal static partial class BundleV1
{
    public const string Formato = "forensic-video/bundle/v1";
    public const string ArchivoManifiesto = "manifiesto.json";
    public const string FirmaDelManifiesto = "manifiesto.json.sig";
    public const string ArchivoCadena = "cadena.json";
    public const string ArchivoSellos = "sellos.json";
    public const string ArchivoPdf = "informe.pdf";
    public const string ArchivoLeeme = "LEEME.txt";

    private static readonly JsonWriterOptions Escritura = new()
    {
        Indented = true,
        // Legible para un perito (acentos tal cual). El contenido es el mismo: JSON escapa sin pérdida.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex("^[a-z0-9]{1,8}$")]
    private static partial Regex ExtensionSegura();

    /// <summary>
    /// <c>evidencias/&lt;id&gt;</c>, con la extensión del nombre declarado si es simple: el nombre nunca
    /// forma la ruta, y el id la hace única aunque dos evidencias tengan el mismo contenido.
    /// </summary>
    public static string RutaDe(Evidencia e)
    {
        var ext = Path.GetExtension(e.NombreOriginal).TrimStart('.').ToLowerInvariant();
        return $"evidencias/{e.Id:D}" + (ExtensionSegura().IsMatch(ext) ? "." + ext : "");
    }

    private static byte[] Json(Action<Utf8JsonWriter> escribir)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, Escritura))
            escribir(w);
        return buffer.ToArray();
    }

    public static byte[] Cadena(Guid casoId, IEnumerable<AsientoDelInforme> asientos) => Json(w =>
    {
        w.WriteStartObject();
        w.WriteString("formato", AsientoDeCustodia.FormatoCanonico);
        w.WriteString("caso", casoId.ToString("D"));
        w.WriteStartArray("asientos");
        foreach (var (a, opaco) in asientos)
        {
            w.WriteStartObject();
            w.WriteNumber("secuencia", a.Secuencia);
            if (opaco)
            {
                w.WriteBoolean("opaco", true);
            }
            else
            {
                w.WriteString("id", a.Id.ToString("D"));
                w.WriteString("tipo", a.Tipo.ToString());
                if (a.EvidenciaId is { } id)
                    w.WriteString("evidencia", id.ToString("D"));
                else
                    w.WriteNull("evidencia");
                w.WriteString("actor", a.Actor);
                w.WriteString("timestamp", AsientoDeCustodia.TimestampCanonico(a.Timestamp));
                // El detalle exacto que se hasheó, como string: el verificador lo hashea tal cual.
                w.WriteString("detalle", a.Detalle);
            }
            w.WriteString("hashAnterior", a.HashAnterior.Hex);
            w.WriteString("hash", a.Hash.Hex);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    });

    public static byte[] Sellos(IEnumerable<SelloDeCadena> sellos) => Json(w =>
    {
        w.WriteStartObject();
        w.WriteString("formato", SelloDeCadena.FormatoCanonico);
        w.WriteStartArray("sellos");
        foreach (var s in sellos.OrderBy(s => s.Secuencia))
        {
            w.WriteStartObject();
            w.WriteString("caso", s.CasoId.ToString("D"));
            w.WriteNumber("secuencia", s.Secuencia);
            w.WriteString("hashCabeza", s.HashCabeza.Hex);
            w.WriteString("selladoEn", AsientoDeCustodia.TimestampCanonico(s.SelladoEn));
            w.WriteString("claveId", s.ClaveId);
            w.WriteString("firma", Convert.ToBase64String(s.Firma));
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    });

    public static byte[] Manifiesto(
        string tipo, Caso caso, DateTimeOffset generadoEn, string generadoPor, string claveId, byte[] clavePublica,
        AsientoDeCustodia cabeza, IReadOnlyList<(string Ruta, byte[] Contenido)> archivos, IReadOnlyList<EvidenciaDelBundle> evidencias) =>
        Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("formato", Formato);
            w.WriteString("tipo", tipo);
            w.WriteStartObject("caso");
            w.WriteString("id", caso.Id.ToString("D"));
            w.WriteString("numeroExpediente", caso.NumeroExpediente);
            w.WriteString("descripcion", caso.Descripcion);
            w.WriteEndObject();
            w.WriteString("generadoEn", AsientoDeCustodia.TimestampCanonico(generadoEn));
            w.WriteString("generadoPor", generadoPor);
            w.WriteStartObject("clave");
            w.WriteString("id", claveId);
            w.WriteString("publica", Convert.ToHexString(clavePublica).ToLowerInvariant());
            w.WriteEndObject();
            w.WriteStartObject("cadena");
            w.WriteNumber("hasta", cabeza.Secuencia);
            w.WriteString("hashCabeza", cabeza.Hash.Hex);
            w.WriteEndObject();
            w.WriteStartArray("archivos");
            foreach (var (ruta, contenido) in archivos)
            {
                w.WriteStartObject();
                w.WriteString("ruta", ruta);
                w.WriteString("sha256", HashSha256.Calcular(contenido).Hex);
                w.WriteNumber("tamanoBytes", contenido.Length);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("evidencias");
            foreach (var e in evidencias)
            {
                w.WriteStartObject();
                w.WriteString("id", e.Evidencia.Id.ToString("D"));
                w.WriteString("sha256", e.Evidencia.Hash.Hex);
                w.WriteNumber("tamanoBytes", e.Evidencia.TamanoBytes);
                w.WriteString("nombre", e.Evidencia.NombreOriginal);
                w.WriteNumber("asientoDeOrigen", e.AsientoDeOrigen);
                w.WriteBoolean("embebida", e.Embebida);
                if (e.Ruta is null)
                    w.WriteNull("ruta");
                else
                    w.WriteString("ruta", e.Ruta);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });

    public static byte[] Leeme(string tipo, Caso caso, string claveId, long hasta, IReadOnlyList<EvidenciaDelBundle> evidencias)
    {
        var aparte = evidencias.Where(e => !e.Embebida).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(tipo == "pericial"
            ? "INFORME PERICIAL: bundle verificable (forensic-video/bundle/v1)"
            : "INFORME INVESTIGATIVO: SÓLO INVESTIGACIÓN, NO ES PRUEBA (forensic-video/bundle/v1)");
        sb.AppendLine();
        sb.AppendLine($"Caso: {caso.NumeroExpediente} ({caso.Id:D}). Cadena de custodia hasta la secuencia {hasta}.");
        sb.AppendLine();
        sb.AppendLine("Este archivo se puede verificar sin el servidor del laboratorio, con el verificador independiente");
        sb.AppendLine("y open-source del proyecto (docs/verificador.md):");
        sb.AppendLine();
        sb.AppendLine($"  verificador --clave <huella> --exigir {tipo} [--externos <carpeta>] <este archivo>");
        sb.AppendLine();
        sb.AppendLine($"La huella de la clave del laboratorio que declara este bundle es {claveId}.");
        sb.AppendLine("No la tome de aquí: pídala al laboratorio por un medio independiente y compárela.");
        if (aparte.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Por su tamaño, estas evidencias se entregan aparte; el verificador las busca por hash en --externos:");
            foreach (var e in aparte)
                sb.AppendLine($"  {e.Evidencia.NombreOriginal}: {e.Evidencia.TamanoBytes} bytes, sha256 {e.Evidencia.Hash.Hex}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString().Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Escribe el ZIP. Cada evidencia embebida se copia desde el almacén con su hash verificado al abrirla.
    /// </summary>
    public static async Task EscribirAsync(
        Stream destino, byte[] manifiesto, byte[] firma, IReadOnlyList<(string Ruta, byte[] Contenido)> archivos,
        IReadOnlyList<EvidenciaDelBundle> evidencias, DateTimeOffset generadoEn,
        Func<HashSha256, CancellationToken, Task<Stream>> abrirVerificado, CancellationToken ct)
    {
        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);

        async Task AgregarAsync(string ruta, Func<Stream, Task> escribir, CompressionLevel nivel)
        {
            var entrada = zip.CreateEntry(ruta, nivel);
            entrada.LastWriteTime = generadoEn;
            await using var s = entrada.Open();
            await escribir(s);
        }

        await AgregarAsync(ArchivoManifiesto, s => s.WriteAsync(manifiesto, ct).AsTask(), CompressionLevel.Optimal);
        await AgregarAsync(FirmaDelManifiesto, s => s.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToBase64String(firma) + "\n"), ct).AsTask(),
            CompressionLevel.NoCompression);
        foreach (var (ruta, contenido) in archivos)
            await AgregarAsync(ruta, s => s.WriteAsync(contenido, ct).AsTask(), CompressionLevel.Optimal);
        foreach (var e in evidencias.Where(e => e.Embebida))
        {
            // Las imágenes y los videos ya vienen comprimidos: se guardan tal cual.
            await AgregarAsync(e.Ruta!, async s =>
            {
                await using var origen = await abrirVerificado(e.Evidencia.Hash, ct);
                await origen.CopyToAsync(s, ct);
            }, CompressionLevel.NoCompression);
        }
    }
}
