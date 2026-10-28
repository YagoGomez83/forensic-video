using Forensic.Application.Casos;
using Forensic.Domain.Custodia;

namespace Forensic.Application.Informes;

public sealed record ArchivoDelInformeDto(string Sha256, long TamanoBytes);

/// <summary>
/// Un informe emitido, leído de su asiento de emisión. <see cref="Hasta"/> es la última secuencia de la
/// cadena que cubre su bundle: la anterior a la del propio asiento de emisión (<see cref="Secuencia"/>).
/// </summary>
public sealed record InformeEmitidoDto(
    string Tipo, long Secuencia, DateTimeOffset EmitidoEn, string EmitidoPor, long Hasta,
    ArchivoDelInformeDto Pdf, ArchivoDelInformeDto Bundle, int Evidencias, int EvidenciasExcluidas, int AsientosOpacos);

/// <summary>Un archivo de un informe para descargar, ya verificado contra el hash asentado.</summary>
public sealed record ArchivoDeInforme(Stream Contenido, string TipoDeContenido, string NombreDeArchivo);

/// <summary>Redacta el PDF legible de un informe. No decide qué contiene: recibe sólo lo que ya pasó la puerta.</summary>
public interface IRedactorDeInformes
{
    byte[] RedactarPericial(InformePericialParaRedactar informe);

    byte[] RedactarInvestigativo(InformeInvestigativoParaRedactar informe);
}

public sealed record InformePericialParaRedactar(
    CasoDto Caso, string EmitidoPor, DateTimeOffset EmitidoEn, string ClaveDelLaboratorio, long Hasta, string HashCabeza,
    SelloDeCadena UltimoSello, IReadOnlyList<EvidenciaParaRedactar> Evidencias, IReadOnlyList<AsientoParaRedactar> Asientos,
    int AsientosOpacos, int EvidenciasExcluidas);

/// <summary>
/// Una evidencia del informe. Un derivado trae la operación que lo produjo (tipo, parámetros y
/// entorno tal como se asentaron) y su entrada.
/// </summary>
public sealed record EvidenciaParaRedactar(
    Guid Id, string Nombre, string Sha256, long TamanoBytes, bool Embebida, long AsientoDeOrigen,
    Guid? EntradaId, string? Operacion, string? Parametros, string? Entorno, bool SoloInvestigacion = false, string? Modelo = null);

/// <summary>
/// Todo el caso, marcado «sólo investigación»: las evidencias de las dos vías, los análisis de IA con
/// sus hallazgos descritos como lo que dijo el modelo, y la cadena completa.
/// </summary>
public sealed record InformeInvestigativoParaRedactar(
    CasoDto Caso, string EmitidoPor, DateTimeOffset EmitidoEn, string ClaveDelLaboratorio, long Hasta, string HashCabeza,
    SelloDeCadena UltimoSello, IReadOnlyList<EvidenciaParaRedactar> Evidencias, IReadOnlyList<AnalisisParaRedactar> Analisis,
    IReadOnlyList<AsientoParaRedactar> Asientos);

public sealed record AnalisisParaRedactar(
    Guid EvidenciaId, string Tipo, string Modelo, string Actor, DateTimeOffset RegistradoEn, IReadOnlyList<string> Hallazgos);

/// <summary>Un asiento de la cadena; si es opaco, sólo trae su secuencia y su hash.</summary>
public sealed record AsientoParaRedactar(
    long Secuencia, bool Opaco, string? Tipo, string? Actor, DateTimeOffset? Timestamp, Guid? EvidenciaId, string Hash);
