namespace Forensic.Cliente.Api;

// Contratos de la API tal como viajan en JSON. Son propios del cliente: no se comparten con el
// servidor a propósito. Los tests de contrato (tests/Forensic.IntegrationTests) los cruzan con el
// servidor real para detectar cualquier desfasaje.

public sealed record TokenEmitido(string Token, DateTimeOffset ExpiraEn);

public enum EstadoDeInicio
{
    Listo,

    /// <summary>Contraseña inicial o reseteada: hasta cambiarla, el servidor no permite otra cosa.</summary>
    DebeCambiarContrasena,
}

public enum EstadoCaso
{
    Abierto = 1,
    Cerrado = 2,
}

public sealed record CasoDto(
    Guid Id, string NumeroExpediente, string Descripcion, EstadoCaso Estado,
    DateTimeOffset AbiertoEn, string AbiertoPor, IReadOnlyList<string> AnalistasAsignados);
