using System.Text.Json;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Operaciones;

namespace Forensic.Application;

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
    void Agregar(OperacionForense operacion);

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

/// <summary>
/// Lo que reporta el worker de ingesta sobre los mismos bytes crudos que guardó el almacén. Su hash
/// es un control cruzado; la fuente de verdad es el del almacén. <see cref="Metadatos"/> es la salida
/// de ffprobe, o null si no pudo leer el formato (la evidencia se custodia igual).
/// </summary>
public sealed record AnalisisDeIngesta(HashSha256 Hash, long TamanoBytes, JsonElement? Metadatos, string? MotivoSinMetadatos);

/// <summary>Worker de ingesta: hash de control y metadatos (ffprobe, sólo lectura). Nunca transcodifica.</summary>
public interface IWorkerDeIngesta
{
    /// <summary>
    /// Analiza el contenido. Lanza <see cref="WorkerDeIngestaNoDisponibleException"/> si el worker
    /// no responde o falla.
    /// </summary>
    Task<AnalisisDeIngesta> AnalizarAsync(Stream contenido, CancellationToken ct);
}
