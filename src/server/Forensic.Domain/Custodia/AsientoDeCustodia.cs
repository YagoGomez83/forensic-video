using System.Globalization;
using System.Text;
using Forensic.Domain.Comun;

namespace Forensic.Domain.Custodia;

public enum TipoAsiento
{
    CasoAbierto = 1,
    AnalistaAsignado = 2,
    CasoCerrado = 3,
    EvidenciaIngestada = 4,
    EvidenciaAccedida = 5,
    ControlCruzadoCompletado = 6,
    OperacionAplicada = 7,
    OperacionReproducida = 8,
    HallazgosRegistrados = 9,
    OperacionIaAplicada = 10,
}

/// <summary>
/// Un eslabón del <see cref="RegistroDeCustodia"/>. Inmutable: su hash cubre todos sus campos
/// y el hash del asiento anterior, así que alterar uno invalida a todos los posteriores.
/// </summary>
public sealed class AsientoDeCustodia
{
    /// <summary>Versión del formato canónico. Cambiarlo exige versionar, nunca reinterpretar asientos viejos.</summary>
    public const string FormatoCanonico = "forensic-video/custodia/v1";

    private AsientoDeCustodia()
    {
        Detalle = null!;
        Actor = null!;
    }

    /// <summary>Rehidratación con valores arbitrarios (tests de manipulación).</summary>
    internal AsientoDeCustodia(
        Guid id, Guid casoId, long secuencia, TipoAsiento tipo, Guid? evidenciaId, string detalle,
        string actor, DateTimeOffset timestamp, HashSha256 hashAnterior, HashSha256 hash)
    {
        Id = id;
        CasoId = casoId;
        Secuencia = secuencia;
        Tipo = tipo;
        EvidenciaId = evidenciaId;
        Detalle = detalle;
        Actor = actor;
        Timestamp = timestamp;
        HashAnterior = hashAnterior;
        Hash = hash;
    }

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }

    /// <summary>Posición en la cadena del caso, desde 1 y sin huecos.</summary>
    public long Secuencia { get; private set; }

    public TipoAsiento Tipo { get; private set; }
    public Guid? EvidenciaId { get; private set; }

    /// <summary>JSON con los datos del evento. Se hashea tal cual está almacenado.</summary>
    public string Detalle { get; private set; }

    public string Actor { get; private set; }

    /// <summary>Timestamp del servidor, UTC con precisión de microsegundos.</summary>
    public DateTimeOffset Timestamp { get; private set; }

    public HashSha256 HashAnterior { get; private set; }
    public HashSha256 Hash { get; private set; }

    internal static AsientoDeCustodia Crear(
        Guid casoId, long secuencia, TipoAsiento tipo, Guid? evidenciaId, string detalle,
        string actor, DateTimeOffset timestamp, HashSha256 hashAnterior)
    {
        var asiento = new AsientoDeCustodia(
            Guid.NewGuid(), casoId, secuencia, tipo, evidenciaId, detalle, actor,
            Tiempo.NormalizarParaCustodia(timestamp), hashAnterior, default);
        asiento.Hash = asiento.CalcularHash();
        return asiento;
    }

    /// <summary>
    /// Recalcula el hash desde los campos. Cada campo va prefijado por su longitud para que
    /// ningún valor (p. ej. un detalle con saltos de línea) pueda simular otro campo.
    /// </summary>
    public HashSha256 CalcularHash()
    {
        var sb = new StringBuilder();
        Campo(sb, "formato", FormatoCanonico);
        Campo(sb, "id", Id.ToString("D"));
        Campo(sb, "caso", CasoId.ToString("D"));
        Campo(sb, "secuencia", Secuencia.ToString(CultureInfo.InvariantCulture));
        Campo(sb, "tipo", Tipo.ToString());
        Campo(sb, "evidencia", EvidenciaId?.ToString("D") ?? "");
        Campo(sb, "actor", Actor);
        Campo(sb, "timestamp", Timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
        Campo(sb, "anterior", HashAnterior.Hex);
        Campo(sb, "detalle", Detalle);
        return HashSha256.Calcular(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static void Campo(StringBuilder sb, string nombre, string valor) =>
        sb.Append(nombre).Append(':')
          .Append(Encoding.UTF8.GetByteCount(valor).ToString(CultureInfo.InvariantCulture)).Append(':')
          .Append(valor).Append('\n');
}
