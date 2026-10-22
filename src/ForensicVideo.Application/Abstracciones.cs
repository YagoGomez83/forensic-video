using ForensicVideo.Domain.Casos;
using ForensicVideo.Domain.Comun;
using ForensicVideo.Domain.Custodia;
using ForensicVideo.Domain.Evidencias;

namespace ForensicVideo.Application;

/// <summary>Actor autenticado de la petición en curso.</summary>
public interface IActorActual
{
    Actor Actor { get; }
}

/// <summary>Persistencia transaccional de casos, evidencias y custodia.</summary>
public interface IPersistenciaForense
{
    Task<Caso?> ObtenerCasoAsync(Guid casoId, CancellationToken ct);
    Task<IReadOnlyList<Caso>> ListarCasosAsync(CancellationToken ct);
    Task<Evidencia?> ObtenerEvidenciaAsync(Guid casoId, Guid evidenciaId, CancellationToken ct);
    Task<IReadOnlyList<Evidencia>> ListarEvidenciasAsync(Guid casoId, CancellationToken ct);
    Task<AsientoDeCustodia?> ObtenerUltimoAsientoAsync(Guid casoId, CancellationToken ct);
    Task<IReadOnlyList<AsientoDeCustodia>> ObtenerCadenaAsync(Guid casoId, CancellationToken ct);

    void Agregar(Caso caso);
    void Agregar(Evidencia evidencia);
    void Agregar(AsientoDeCustodia asiento);

    /// <summary>
    /// Confirma todos los cambios en una sola transacción. Si otro proceso asentó en la misma
    /// cadena primero, lanza <see cref="ConflictoDeCustodiaException"/> y no se guarda nada.
    /// </summary>
    Task GuardarCambiosAsync(CancellationToken ct);
}

public sealed record ContenidoAlmacenado(HashSha256 Hash, long TamanoBytes);

/// <summary>Almacén de blobs de evidencia, direccionado por contenido. Los blobs son inmutables.</summary>
public interface IAlmacenDeEvidencia
{
    /// <summary>Guarda el contenido calculando su SHA-256 mientras se escribe.</summary>
    Task<ContenidoAlmacenado> GuardarAsync(Stream contenido, CancellationToken ct);

    /// <summary>
    /// Abre el blob verificando antes que su hash coincida. Lanza
    /// <see cref="IntegridadComprometidaException"/> si no coincide o si falta.
    /// </summary>
    Task<Stream> AbrirVerificadoAsync(HashSha256 hash, CancellationToken ct);
}
