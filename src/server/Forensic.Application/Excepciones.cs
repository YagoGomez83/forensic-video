using Forensic.Domain.Comun;

namespace Forensic.Application;

public sealed class NoEncontradoException(string mensaje) : Exception(mensaje);

/// <summary>Dos asientos compitieron por la misma posición de la cadena. El cliente puede reintentar.</summary>
public sealed class ConflictoDeCustodiaException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>El worker de ingesta rechazó el contenido (su <c>/v1/analizar</c> respondió 422).</summary>
public sealed class IngestaRechazadaException(string mensaje) : Exception(mensaje);

/// <summary>
/// El worker reportó un hash o tamaño distinto de los bytes que el servidor le mandó, ya verificados
/// contra el almacén. No es un rechazo de contenido: es corrupción en tránsito o un worker roto o
/// comprometido, un evento de seguridad.
/// </summary>
public sealed class DivergenciaDeControlCruzadoException(string mensaje) : Exception(mensaje);

/// <summary>El worker de ingesta no respondió, falló o devolvió una respuesta inválida.</summary>
public sealed class WorkerDeIngestaNoDisponibleException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>El blob almacenado no coincide con el hash registrado: la evidencia queda bloqueada.</summary>
public sealed class IntegridadComprometidaException(HashSha256 esperado, string motivo)
    : Exception($"Integridad comprometida de la evidencia {esperado}: {motivo}")
{
    public HashSha256 HashEsperado { get; } = esperado;
}

/// <summary>
/// Login rechazado. El mensaje es siempre el mismo: no revela si falló el usuario, la contraseña o
/// si la cuenta está bloqueada o deshabilitada.
/// </summary>
public sealed class CredencialesInvalidasException(Exception? interna = null)
    : Exception("Usuario o contraseña incorrectos.", interna);

/// <summary>Otro proceso modificó el mismo usuario primero. El cliente puede reintentar.</summary>
public sealed class ConflictoDeIdentidadException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>
/// La cadena de custodia del caso no verifica: nada que dependa de sus asientos (como reproducir una
/// operación) puede confiar en ella.
/// </summary>
public sealed class CadenaComprometidaException(string mensaje) : Exception(mensaje);

/// <summary>El worker de procesamiento rechazó el contenido o los parámetros (su API respondió 422).</summary>
public sealed class ProcesamientoRechazadoException(string mensaje) : Exception(mensaje);

/// <summary>
/// El worker de procesamiento no respondió, falló o devolvió una respuesta inválida. A diferencia de
/// la ingesta, no hay estado pendiente: sin operación no hay derivado.
/// </summary>
public sealed class WorkerDeProcesamientoNoDisponibleException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);
