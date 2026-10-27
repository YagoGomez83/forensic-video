using System.Text.Json;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Operaciones;

namespace Forensic.Domain.Ia;

/// <summary>
/// Una operación de IA leída de su asiento <see cref="TipoAsiento.OperacionIaAplicada"/>: procedencia
/// para el linaje, no una receta de reproducción (eso es <see cref="OperacionAsentada"/>, sólo para lo
/// determinista).
/// </summary>
public sealed record OperacionIaAsentada(
    string Tipo, string Parametros, ModeloDeIa Modelo, Guid EvidenciaEntradaId, HashSha256 HashEntrada,
    Guid EvidenciaSalidaId, HashSha256 HashSalida, string Entorno, ViaProcesamiento Via)
{
    public static OperacionIaAsentada Desde(AsientoDeCustodia asiento)
    {
        if (asiento.Tipo != TipoAsiento.OperacionIaAplicada)
            throw new ExcepcionDeDominio($"El asiento {asiento.Secuencia} no es de una operación de IA.");
        try
        {
            using var doc = JsonDocument.Parse(asiento.Detalle);
            var r = doc.RootElement;
            var formato = r.GetProperty("formato").GetString();
            if (formato != OperacionForense.FormatoDeDetalleIa)
                throw new ExcepcionDeDominio($"Formato de operación de IA desconocido: '{formato}'.");
            var modelo = r.GetProperty("modelo");
            var entrada = r.GetProperty("entrada");
            var salida = r.GetProperty("salida");
            return new OperacionIaAsentada(
                r.GetProperty("operacion").GetString()!,
                r.GetProperty("parametros").GetRawText(),
                new ModeloDeIa(
                    modelo.GetProperty("id").GetString()!, modelo.GetProperty("version").GetString()!,
                    HashSha256.Parse(modelo.GetProperty("sha256").GetString()!)),
                entrada.GetProperty("evidenciaId").GetGuid(),
                HashSha256.Parse(entrada.GetProperty("sha256").GetString()!),
                salida.GetProperty("evidenciaId").GetGuid(),
                HashSha256.Parse(salida.GetProperty("sha256").GetString()!),
                r.GetProperty("procedencia").GetProperty("entorno").GetRawText(),
                Enum.Parse<ViaProcesamiento>(r.GetProperty("via").GetString()!));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ExcepcionDeDominio($"El asiento {asiento.Secuencia} no tiene un detalle de operación de IA válido: {ex.Message}");
        }
    }
}
