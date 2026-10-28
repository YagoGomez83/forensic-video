using System.Globalization;
using System.Text.Json;
using Forensic.Application;
using Forensic.Application.Custodia;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forensic.Infrastructure.Sellado;

public sealed class OpcionesSellado
{
    public const string Seccion = "Custodia:Sellado";

    /// <summary>
    /// Clave privada Ed25519 en PKCS#8 PEM. Debe ser un secreto montado (Docker secret), nunca un
    /// archivo del repo ni de la imagen.
    /// </summary>
    public string RutaClavePrivada { get; set; } = "";

    /// <summary>
    /// Directorio de los sellos. Va en un volumen separado de la base y de la evidencia, fuera del
    /// alcance de quien administra PostgreSQL.
    /// </summary>
    public string RaizDeSellos { get; set; } = "";

    /// <summary>Cada cuánto se sellan los casos con asientos nuevos. Cero lo apaga (sólo tests).</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Sellos en disco, uno por archivo (<c>raiz/&lt;caso&gt;/&lt;secuencia&gt;.json</c>), escritos una
/// sola vez: se escriben aparte y se mueven sin sobreescribir, y quedan de solo lectura. La
/// protección real es del despliegue (un volumen que el usuario de la base no pueda escribir); lo
/// que se edite igual en disco lo delata la firma.
/// </summary>
internal sealed class AlmacenDeSellosEnDisco(IOptions<OpcionesSellado> opciones, ILogger<AlmacenDeSellosEnDisco> logger) : IAlmacenDeSellos
{
    private const string FormatoTimestamp = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _raiz = Path.GetFullPath(
        string.IsNullOrWhiteSpace(opciones.Value.RaizDeSellos)
            ? throw new InvalidOperationException($"Falta configurar {OpcionesSellado.Seccion}:RaizDeSellos.")
            : opciones.Value.RaizDeSellos);

    private sealed record SelloEnDisco(
        string Formato, Guid Caso, long Secuencia, string HashCabeza, string SelladoEn, string ClaveId, string Firma);

    public async Task<bool> AgregarAsync(SelloDeCadena sello, CancellationToken ct)
    {
        var ruta = RutaDe(sello.CasoId, sello.Secuencia);
        if (File.Exists(ruta))
            return false;

        var temporal = Path.Combine(Directory.CreateDirectory(Path.Combine(_raiz, ".escritura")).FullName, $"{Guid.NewGuid():N}.tmp");
        try
        {
            var enDisco = new SelloEnDisco(
                SelloDeCadena.FormatoCanonico, sello.CasoId, sello.Secuencia, sello.HashCabeza.Hex,
                sello.SelladoEn.UtcDateTime.ToString(FormatoTimestamp, CultureInfo.InvariantCulture),
                sello.ClaveId, Convert.ToBase64String(sello.Firma));
            await using (var destino = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(destino, enDisco, OpcionesJson, ct);
                destino.Flush(flushToDisk: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            try
            {
                File.Move(temporal, ruta, overwrite: false);
            }
            catch (IOException) when (File.Exists(ruta))
            {
                // Otro sellado de la misma secuencia llegó primero: el suyo queda.
                return false;
            }
            File.SetAttributes(ruta, FileAttributes.ReadOnly);
            return true;
        }
        finally
        {
            if (File.Exists(temporal))
                File.Delete(temporal);
        }
    }

    public async Task<IReadOnlyList<SelloDeCadena>> ListarAsync(Guid casoId, CancellationToken ct)
    {
        var dir = Path.Combine(_raiz, casoId.ToString("D"));
        if (!Directory.Exists(dir))
            return [];

        var sellos = new List<SelloDeCadena>();
        foreach (var archivo in Directory.EnumerateFiles(dir, "*.json"))
            sellos.Add(await LeerAsync(archivo, ct));
        return sellos.OrderBy(s => s.Secuencia).ToList();
    }

    private async Task<SelloDeCadena> LeerAsync(string archivo, CancellationToken ct)
    {
        try
        {
            await using var origen = File.OpenRead(archivo);
            var s = await JsonSerializer.DeserializeAsync<SelloEnDisco>(origen, OpcionesJson, ct)
                ?? throw new FormatException("vacío");
            if (s.Formato != SelloDeCadena.FormatoCanonico)
                throw new FormatException($"formato {s.Formato}");
            return SelloDeCadena.Rehidratar(
                s.Caso, s.Secuencia, HashSha256.Parse(s.HashCabeza),
                DateTimeOffset.ParseExact(s.SelladoEn, FormatoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                s.ClaveId, Convert.FromBase64String(s.Firma));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ExcepcionDeDominio or ArgumentException)
        {
            logger.LogCritical(ex, "Sello ilegible en el almacén de sellos: {Archivo}", archivo);
            throw new CadenaComprometidaException($"Hay un sello ilegible en el almacén ({Path.GetFileName(archivo)}).");
        }
    }

    private string RutaDe(Guid casoId, long secuencia) =>
        Path.Combine(_raiz, casoId.ToString("D"), secuencia.ToString("D20", CultureInfo.InvariantCulture) + ".json");
}
