using ForensicVideo.Domain.Casos;
using ForensicVideo.Domain.Comun;
using ForensicVideo.Domain.Operaciones;

namespace ForensicVideo.Domain.Evidencias;

/// <summary>
/// Un original ingestado o un derivado producido por una <see cref="OperacionForense"/>.
/// Nunca se modifica: su hash es su identidad para siempre.
/// </summary>
public sealed class Evidencia
{
    public const int LongitudMaximaNombre = 255;

    private Evidencia()
    {
        NombreOriginal = null!;
        IngestadaPor = null!;
    }

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public HashSha256 Hash { get; private set; }
    public long TamanoBytes { get; private set; }

    /// <summary>Nombre declarado al ingestar. Es metadato: nunca se usa para construir rutas.</summary>
    public string NombreOriginal { get; private set; }

    public ViaProcesamiento Via { get; private set; }

    /// <summary>Evidencia de la que deriva; <c>null</c> en un original.</summary>
    public Guid? PadreId { get; private set; }

    public Guid? OperacionOrigenId { get; private set; }
    public DateTimeOffset IngestadaEn { get; private set; }
    public string IngestadaPor { get; private set; }

    public bool EsOriginal => PadreId is null;

    /// <summary>
    /// Ingesta de un original. Recibe el hash ya calculado: sin hash no hay ingesta.
    /// </summary>
    public static Evidencia Ingestar(
        Caso caso, HashSha256 hash, long tamanoBytes, string nombreOriginal, Actor actor, DateTimeOffset ahora)
    {
        caso.ExigirOperacion(actor);
        ValidarContenido(hash, tamanoBytes);
        ValidarNombre(nombreOriginal);

        return new Evidencia
        {
            Id = Guid.NewGuid(),
            CasoId = caso.Id,
            Hash = hash,
            TamanoBytes = tamanoBytes,
            NombreOriginal = nombreOriginal,
            Via = ViaProcesamiento.AdmisibleEnJuicio,
            IngestadaEn = Tiempo.NormalizarParaCustodia(ahora),
            IngestadaPor = actor.Usuario,
        };
    }

    /// <summary>
    /// Crea el derivado que resulta de aplicar <paramref name="operacion"/> a esta evidencia.
    /// La vía se propaga: un derivado de algo «sólo investigación» nunca vuelve a ser admisible.
    /// </summary>
    public Evidencia Derivar(OperacionForense operacion, string nombre)
    {
        if (operacion.EvidenciaEntradaId != Id || operacion.HashEntrada != Hash)
            throw new ExcepcionDeDominio("La operación no se aplicó sobre esta evidencia.");
        if (operacion.HashSalida == Hash)
            throw new ExcepcionDeDominio("Un derivado idéntico al padre no es un derivado nuevo.");
        ValidarNombre(nombre);

        return new Evidencia
        {
            Id = Guid.NewGuid(),
            CasoId = CasoId,
            Hash = operacion.HashSalida,
            TamanoBytes = operacion.TamanoSalidaBytes,
            NombreOriginal = nombre,
            Via = Via.Propagar(operacion.Via),
            PadreId = Id,
            OperacionOrigenId = operacion.Id,
            IngestadaEn = operacion.RegistradaEn,
            IngestadaPor = operacion.Actor,
        };
    }

    public void ExigirAdmisibleEnJuicio()
    {
        if (Via != ViaProcesamiento.AdmisibleEnJuicio)
            throw new ExcepcionDeDominio(
                $"La evidencia {Id} es «sólo investigación» y no puede presentarse como prueba.");
    }

    internal static void ValidarContenido(HashSha256 hash, long tamanoBytes)
    {
        if (!hash.EstaInicializado)
            throw new ExcepcionDeDominio("Sin hash no hay evidencia.");
        if (tamanoBytes <= 0)
            throw new ExcepcionDeDominio("Una evidencia vacía no es admisible.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > LongitudMaximaNombre || nombre.Any(char.IsControl))
            throw new ExcepcionDeDominio(
                $"El nombre es obligatorio, de hasta {LongitudMaximaNombre} caracteres y sin caracteres de control.");
    }
}
