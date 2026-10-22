using Forensic.Domain.Comun;

namespace Forensic.Application;

public sealed class NoEncontradoException(string mensaje) : Exception(mensaje);

/// <summary>Dos asientos compitieron por la misma posición de la cadena. El cliente puede reintentar.</summary>
public sealed class ConflictoDeCustodiaException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>El control cruzado del worker no coincide con lo que guardó el servidor: la ingesta no ocurre.</summary>
public sealed class IngestaRechazadaException(string mensaje) : Exception(mensaje);

/// <summary>Sin control cruzado no hay ingesta: el worker no respondió o falló.</summary>
public sealed class WorkerDeIngestaNoDisponibleException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>El blob almacenado no coincide con el hash registrado: la evidencia queda bloqueada.</summary>
public sealed class IntegridadComprometidaException(HashSha256 esperado, string motivo)
    : Exception($"Integridad comprometida de la evidencia {esperado}: {motivo}")
{
    public HashSha256 HashEsperado { get; } = esperado;
}
