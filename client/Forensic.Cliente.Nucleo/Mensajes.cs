using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo;

/// <summary>
/// Qué ve el analista ante un error. El texto es el del servidor; sólo se agrega contexto donde
/// la reacción del analista cambia (una divergencia es un evento de seguridad, no un error común).
/// </summary>
public static class Mensajes
{
    public static string Para(ErrorDeApi error) => error.Tipo switch
    {
        TipoDeError.DivergenciaDeSeguridad =>
            $"ALERTA DE SEGURIDAD: el servidor detectó una divergencia con el worker y no aceptó el resultado. {error.Message}",
        TipoDeError.WorkerNoDisponible => $"El servicio de procesamiento no está disponible. {error.Message}",
        _ => error.Message,
    };
}
