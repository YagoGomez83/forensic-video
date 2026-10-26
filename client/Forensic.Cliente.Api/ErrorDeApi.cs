using System.Net;

namespace Forensic.Cliente.Api;

/// <summary>
/// Qué salió mal, tal como lo decidió el servidor. El cliente no reinterpreta: muestra el detalle que
/// mandó el servidor y distingue sólo lo que la pantalla necesita para reaccionar.
/// </summary>
public enum TipoDeError
{
    ServidorInalcanzable,
    CredencialesInvalidas,
    SesionExpirada,
    AccesoDenegado,
    NoEncontrado,
    Conflicto,
    Rechazado,
    IntegridadComprometida,
    DemasiadosIntentos,

    /// <summary>502: el worker reportó algo distinto de lo que verificó el servidor. Evento de seguridad.</summary>
    DivergenciaDeSeguridad,

    WorkerNoDisponible,
    ErrorDelServidor,
}

public class ErrorDeApi(TipoDeError tipo, HttpStatusCode? estado, string mensaje, Exception? interna = null)
    : Exception(mensaje, interna)
{
    public TipoDeError Tipo { get; } = tipo;
    public HttpStatusCode? Estado { get; } = estado;
}

/// <summary>El servidor rechazó el token de una sesión activa: hay que volver al login.</summary>
public sealed class SesionExpiradaException(string mensaje)
    : ErrorDeApi(TipoDeError.SesionExpirada, HttpStatusCode.Unauthorized, mensaje);
