using System.Security.Cryptography;
using System.Text;
using Forensic.Application;
using Forensic.Application.Operaciones;
using Forensic.Domain.Comun;

namespace Forensic.IntegrationTests;

/// <summary>
/// Doble del worker de procesamiento. El servidor no decodifica imágenes, así que alcanza con una
/// salida determinista que dependa de la operación, los parámetros y la entrada: reproducir da los
/// mismos bytes. Con los modos se simula un worker que ve otra entrada, reporta otra salida, rechaza
/// el contenido o no responde.
/// </summary>
public sealed class WorkerDeProcesamientoDoble(WorkerDeProcesamientoDoble.Modo modo = WorkerDeProcesamientoDoble.Modo.Correcto)
    : IWorkerDeProcesamiento
{
    public enum Modo
    {
        Correcto,
        DiscrepaEntrada,
        DiscrepaSalida,
        Rechaza,
        Caido,

        /// <summary>Otra salida, reportada con su hash correcto: un entorno que ya no reproduce.</summary>
        Deriva,
    }

    public const string Entorno = """{"hilos":1,"opencv":"doble","optimizado":false,"worker":"doble"}""";
    public const string EntornoDistinto = """{"hilos":1,"opencv":"doble-2","optimizado":false,"worker":"doble"}""";

    private int _llamadas;

    public int Llamadas => _llamadas;

    public static byte[] SalidaPara(string operacion, string parametros, byte[] entrada) =>
        Encoding.UTF8.GetBytes($"derivado|{operacion}|{parametros}|{Convert.ToHexString(SHA256.HashData(entrada))}");

    public async Task<SalidaDeProcesamiento> AplicarAsync(string operacion, string parametros, Stream entrada, CancellationToken ct)
    {
        Interlocked.Increment(ref _llamadas);
        using var copia = new MemoryStream();
        await entrada.CopyToAsync(copia, ct);
        var bytes = copia.ToArray();

        switch (modo)
        {
            case Modo.Caido:
                throw new WorkerDeProcesamientoNoDisponibleException("El doble está caído.");
            case Modo.Rechaza:
                throw new ProcesamientoRechazadoException("La región se sale de la imagen.");
        }

        var salida = SalidaPara(operacion, parametros, bytes);
        if (modo == Modo.Deriva)
            salida = [.. salida, 0x42];
        var hashEntrada = HashSha256.Calcular(modo == Modo.DiscrepaEntrada ? [.. bytes, 0xFF] : bytes);
        var hashSalida = HashSha256.Calcular(modo == Modo.DiscrepaSalida ? [.. salida, 0xFF] : salida);
        return new SalidaDeProcesamiento(
            new MemoryStream(salida), hashEntrada, hashSalida, HashSha256.Calcular([.. salida, 0x01]).Hex,
            modo == Modo.Deriva ? EntornoDistinto : Entorno);
    }
}
