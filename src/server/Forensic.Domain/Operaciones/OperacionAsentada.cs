using System.Text.Json;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Una operación leída de su asiento <see cref="TipoAsiento.OperacionAplicada"/>. Basta el asiento
/// (no hace falta ninguna otra tabla) para re-ejecutarla: tipo, parámetros exactos, entrada y entorno.
/// </summary>
public sealed record OperacionAsentada(
    string Formato,
    string Tipo,
    string Parametros,
    Guid EvidenciaEntradaId,
    HashSha256 HashEntrada,
    Guid EvidenciaSalidaId,
    HashSha256 HashSalida,
    long TamanoSalidaBytes,
    string? HashPixelesSalida,
    string Entorno,
    ViaProcesamiento Via)
{
    public static OperacionAsentada Desde(AsientoDeCustodia asiento)
    {
        if (asiento.Tipo != TipoAsiento.OperacionAplicada)
            throw new ExcepcionDeDominio($"El asiento {asiento.Secuencia} no es de una operación aplicada.");
        try
        {
            using var doc = JsonDocument.Parse(asiento.Detalle);
            var r = doc.RootElement;
            var formato = r.GetProperty("formato").GetString();
            if (formato != OperacionForense.FormatoDeDetalle)
                throw new ExcepcionDeDominio($"Formato de operación desconocido: '{formato}'.");
            var entrada = r.GetProperty("entrada");
            var salida = r.GetProperty("salida");
            return new OperacionAsentada(
                formato,
                r.GetProperty("operacion").GetString()!,
                // GetRawText devuelve el texto exacto del asiento, sin re-serializar.
                r.GetProperty("parametros").GetRawText(),
                entrada.GetProperty("evidenciaId").GetGuid(),
                HashSha256.Parse(entrada.GetProperty("sha256").GetString()!),
                salida.GetProperty("evidenciaId").GetGuid(),
                HashSha256.Parse(salida.GetProperty("sha256").GetString()!),
                salida.GetProperty("tamanoBytes").GetInt64(),
                salida.GetProperty("sha256Pixeles").GetString(),
                r.GetProperty("entorno").GetRawText(),
                Enum.Parse<ViaProcesamiento>(r.GetProperty("via").GetString()!));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ExcepcionDeDominio($"El asiento {asiento.Secuencia} no tiene un detalle de operación válido: {ex.Message}");
        }
    }
}
