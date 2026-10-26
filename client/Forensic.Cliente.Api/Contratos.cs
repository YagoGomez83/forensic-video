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

public enum ViaProcesamiento
{
    AdmisibleEnJuicio = 1,
    SoloInvestigacion = 2,
}

public sealed record EvidenciaDto(
    Guid Id, Guid CasoId, string Hash, long TamanoBytes, string NombreOriginal, ViaProcesamiento Via,
    Guid? PadreId, DateTimeOffset IngestadaEn, string IngestadaPor, bool ControlCruzadoPendiente);

public sealed record AsientoDto(
    long Secuencia, Guid Id, string Tipo, Guid? EvidenciaId, string Detalle, string Actor,
    DateTimeOffset Timestamp, string HashAnterior, string Hash);

/// <summary>El veredicto del servidor sobre la cadena. El cliente lo muestra; no lo recalcula.</summary>
public sealed record VerificacionDto(bool EsIntegra, int CantidadAsientos, string? HashFinal, long? SecuenciaRota, string? Motivo);

public sealed record ExportDeCustodia(
    string Formato, DateTimeOffset GeneradoEn, string GeneradoPor, CasoDto Caso,
    IReadOnlyList<AsientoDto> Asientos, VerificacionDto Verificacion, int CrucesPendientes);

/// <param name="Parametros">Los parámetros canónicos tal como quedaron asentados en el servidor.</param>
public sealed record OperacionAplicadaDto(EvidenciaDto Derivado, Guid OperacionId, string Tipo, string Parametros, string Entorno);
