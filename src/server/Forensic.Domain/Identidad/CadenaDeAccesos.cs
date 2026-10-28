using System.Globalization;
using System.Text;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;

namespace Forensic.Domain.Identidad;

/// <summary>Un evento de la auditoría de accesos con su eslabón de la cadena.</summary>
/// <param name="Tipo">El nombre del tipo de evento, tal como se guarda (p. ej. <c>LoginFallido</c>).</param>
public sealed record AccesoEncadenado(
    long Secuencia, string Tipo, string Usuario, string Actor, string? Detalle, string? Origen, DateTimeOffset Timestamp,
    HashSha256 HashAnterior, HashSha256 Hash);

/// <summary>
/// La auditoría de accesos encadenada por hash. No es la cadena de custodia (otro schema, otro formato,
/// otra cadena): un evento de identidad nunca es un asiento de custodia. La base encadena cada fila al
/// insertarla (trigger de la migración CadenaDeAccesos, con el mismo formato); esto lo recalcula de forma
/// independiente para verificarla.
/// </summary>
public static class CadenaDeAccesos
{
    /// <summary>Versión del formato canónico. Cambiarlo exige versionar, igual que el de custodia.</summary>
    public const string FormatoCanonico = "forensic-video/accesos/v1";

    /// <summary>
    /// Id con el que se sella esta cadena en el almacén de sellos, junto a las de los casos. No es el id
    /// de ningún caso.
    /// </summary>
    public static readonly Guid IdParaSellos = Guid.Parse("00000000-0000-0000-0000-0000000acce5");

    /// <summary>
    /// Campos prefijados por longitud, como en la custodia; un valor nulo se escribe con longitud -1 para
    /// no confundirlo con uno vacío.
    /// </summary>
    public static HashSha256 CalcularHash(AccesoEncadenado a)
    {
        var sb = new StringBuilder();
        Campo(sb, "formato", FormatoCanonico);
        Campo(sb, "secuencia", a.Secuencia.ToString(CultureInfo.InvariantCulture));
        Campo(sb, "tipo", a.Tipo);
        Campo(sb, "usuario", a.Usuario);
        Campo(sb, "actor", a.Actor);
        Campo(sb, "detalle", a.Detalle);
        Campo(sb, "origen", a.Origen);
        Campo(sb, "timestamp", AsientoDeCustodia.TimestampCanonico(a.Timestamp));
        Campo(sb, "anterior", a.HashAnterior.Hex);
        return HashSha256.Calcular(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>Verifica la cadena completa, ordenada por secuencia desde 1.</summary>
    public static VerificacionDeCadena Verificar(IReadOnlyList<AccesoEncadenado> cadena)
    {
        var anterior = HashSha256.Genesis;
        for (var i = 0; i < cadena.Count; i++)
        {
            var a = cadena[i];
            if (a.Secuencia != i + 1L)
                return VerificacionDeCadena.Rota(a.Secuencia, $"Secuencia {a.Secuencia} donde se esperaba {i + 1}: falta o sobra un evento.");
            if (a.HashAnterior != anterior)
                return VerificacionDeCadena.Rota(a.Secuencia, "El hash anterior no coincide con el evento previo.");
            if (CalcularHash(a) != a.Hash)
                return VerificacionDeCadena.Rota(a.Secuencia, "El contenido del evento no coincide con su hash: fue alterado.");
            anterior = a.Hash;
        }
        return VerificacionDeCadena.Integra(cadena.Count, anterior);
    }

    private static void Campo(StringBuilder sb, string nombre, string? valor)
    {
        sb.Append(nombre).Append(':');
        if (valor is null)
            sb.Append("-1:");
        else
            sb.Append(Encoding.UTF8.GetByteCount(valor).ToString(CultureInfo.InvariantCulture)).Append(':').Append(valor);
        sb.Append('\n');
    }
}
