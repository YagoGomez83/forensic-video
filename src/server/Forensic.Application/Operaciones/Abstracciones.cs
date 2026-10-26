using Forensic.Domain.Comun;

namespace Forensic.Application.Operaciones;

/// <summary>
/// Lo que devuelve el worker de procesamiento: el derivado y lo que el worker dice de él. Sus hashes
/// son un control cruzado; la fuente de verdad es el hash que calcula el almacén al guardar.
/// </summary>
/// <param name="HashEntrada">Hash de la entrada tal como la recibió el worker.</param>
/// <param name="HashPixeles">Hash de los píxeles de la salida, independiente del encoding.</param>
/// <param name="Entorno">Entorno de ejecución en JSON, tal como lo reportó (va al asiento sin tocar).</param>
public sealed class SalidaDeProcesamiento(
    Stream contenido, HashSha256 hashEntrada, HashSha256 hashSalida, string hashPixeles, string entorno, IDisposable? recursos = null)
    : IAsyncDisposable
{
    public Stream Contenido { get; } = contenido;
    public HashSha256 HashEntrada { get; } = hashEntrada;
    public HashSha256 HashSalida { get; } = hashSalida;
    public string HashPixeles { get; } = hashPixeles;
    public string Entorno { get; } = entorno;

    public async ValueTask DisposeAsync()
    {
        await Contenido.DisposeAsync();
        recursos?.Dispose();
    }
}

/// <summary>Worker de procesamiento: operaciones deterministas de OpenCV que devuelven un PNG.</summary>
public interface IWorkerDeProcesamiento
{
    /// <summary>
    /// Aplica <paramref name="operacion"/> con <paramref name="parametros"/> (el JSON canónico exacto)
    /// sobre <paramref name="entrada"/>. Lanza <see cref="ProcesamientoRechazadoException"/> si el worker
    /// rechaza el contenido o los parámetros, y <see cref="WorkerDeProcesamientoNoDisponibleException"/>
    /// si no responde, falla o devuelve una respuesta inválida.
    /// </summary>
    Task<SalidaDeProcesamiento> AplicarAsync(string operacion, string parametros, Stream entrada, CancellationToken ct);
}
