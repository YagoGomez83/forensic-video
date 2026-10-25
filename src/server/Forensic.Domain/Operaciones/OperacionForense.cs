using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Un paso de procesamiento aplicado a una evidencia: qué se aplicó (tipo y parámetros canónicos),
/// sobre qué entrada, en qué entorno y qué salió. Es todo lo que hace falta para reproducirla.
/// Inmutable una vez registrada.
/// </summary>
public sealed class OperacionForense
{
    private OperacionForense()
    {
        Tipo = null!;
        Entorno = null!;
        Parametros = null!;
        Actor = null!;
    }

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid EvidenciaEntradaId { get; private set; }
    public HashSha256 HashEntrada { get; private set; }

    /// <summary>Tipo versionado del catálogo, p. ej. <c>opencv.recorte/v1</c>.</summary>
    public string Tipo { get; private set; }

    /// <summary>
    /// Entorno del worker que la ejecutó (versiones de OpenCV, numpy, Python, hilos, CPU), en JSON tal
    /// como lo reportó. Una reproducción en otro entorno no está garantizada.
    /// </summary>
    public string Entorno { get; private set; }

    /// <summary>Parámetros en JSON canónico: el string exacto que se envió y que se reenvía al reproducir.</summary>
    public string Parametros { get; private set; }

    public ViaProcesamiento Via { get; private set; }
    public HashSha256 HashSalida { get; private set; }
    public long TamanoSalidaBytes { get; private set; }

    /// <summary>Hash de los píxeles de la salida, independiente del encoding: diagnóstico de reproducción.</summary>
    public string? HashPixelesSalida { get; private set; }
    public string Actor { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }

    public static OperacionForense Registrar(
        Caso caso,
        Evidencia entrada,
        DefinicionDeOperacion operacion,
        string entorno,
        HashSha256 hashSalida,
        long tamanoSalidaBytes,
        string? hashPixelesSalida,
        Actor actor,
        DateTimeOffset ahora)
    {
        if (entrada.CasoId != caso.Id)
            throw new ExcepcionDeDominio("La evidencia de entrada no pertenece al caso.");
        caso.ExigirOperacion(actor);
        ArgumentNullException.ThrowIfNull(operacion);
        if (string.IsNullOrWhiteSpace(entorno))
            throw new ExcepcionDeDominio("Toda operación declara el entorno que la ejecutó: sin él no es reproducible.");
        Evidencia.ValidarContenido(hashSalida, tamanoSalidaBytes);

        return new OperacionForense
        {
            Id = Guid.NewGuid(),
            CasoId = caso.Id,
            EvidenciaEntradaId = entrada.Id,
            HashEntrada = entrada.Hash,
            Tipo = operacion.Tipo,
            Entorno = entorno,
            Parametros = operacion.ParametrosCanonicos,
            // Cualquier operación con IA saca al derivado de la vía judicial.
            Via = operacion.UsaIa ? ViaProcesamiento.SoloInvestigacion : ViaProcesamiento.AdmisibleEnJuicio,
            HashSalida = hashSalida,
            TamanoSalidaBytes = tamanoSalidaBytes,
            HashPixelesSalida = hashPixelesSalida,
            Actor = actor.Usuario,
            RegistradaEn = Tiempo.NormalizarParaCustodia(ahora),
        };
    }
}
