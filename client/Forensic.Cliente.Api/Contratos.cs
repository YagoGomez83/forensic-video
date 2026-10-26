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

public sealed record OperacionDelLinaje(
    long Secuencia, string Tipo, string Parametros, Guid EvidenciaEntradaId, string Entorno, ViaProcesamiento Via,
    string Actor, DateTimeOffset Timestamp);

public sealed record ReproduccionDelLinaje(long Secuencia, bool Reproducido, bool EntornoCoincide, string Actor, DateTimeOffset Timestamp);

public sealed record EslabonDeLinaje(EvidenciaDto Evidencia, OperacionDelLinaje? Operacion, IReadOnlyList<ReproduccionDelLinaje> Reproducciones);

/// <summary>El veredicto del servidor al re-ejecutar una operación desde su asiento.</summary>
public sealed record ResultadoDeReproduccion(
    Guid EvidenciaId, bool Reproducido, bool EntornoCoincide,
    string Sha256Esperado, string Sha256Obtenido, string? Sha256PixelesEsperado, string Sha256PixelesObtenido,
    string EntornoAsentado, string EntornoActual);

public sealed record RegionDto(int X, int Y, int Ancho, int Alto);

public sealed record ModeloDeIaDto(string Id, string Version, string Sha256);

/// <summary>
/// Lo que el modelo reportó: una pista, nunca un dato verificado. <see cref="TextoLeido"/> es lo que el
/// modelo leyó con <see cref="Confianza"/>, no lo que dice la imagen.
/// </summary>
/// <param name="Crudo">El dato crudo del modelo, en JSON tal como lo asentó el servidor.</param>
public sealed record HallazgoDto(
    int Orden, RegionDto Region, string? Clase, string? TextoLeido, decimal Confianza, string Crudo, ViaProcesamiento Via);

/// <summary>Un análisis de IA asentado: siempre «sólo investigación».</summary>
/// <param name="Entorno">Declarado por la workstation que lo corrió: el servidor no lo puede verificar.</param>
public sealed record AnalisisIaDto(
    Guid Id, Guid EvidenciaId, string Analisis, string Parametros, ModeloDeIaDto Modelo, string Entorno, string Actor,
    DateTimeOffset RegistradoEn, ViaProcesamiento Via, IReadOnlyList<HallazgoDto> Hallazgos);
