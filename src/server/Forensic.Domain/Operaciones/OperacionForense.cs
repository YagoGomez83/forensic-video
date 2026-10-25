using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Un paso de procesamiento aplicado a una evidencia. Inmutable una vez registrada.
/// Fase 1 sólo la modela; la persistencia y el registro desde los workers llegan en la Fase 2.
/// </summary>
public sealed class OperacionForense
{
    private OperacionForense()
    {
        Tipo = null!;
        VersionMotor = null!;
        Parametros = null!;
        Actor = null!;
    }

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid EvidenciaEntradaId { get; private set; }
    public HashSha256 HashEntrada { get; private set; }

    /// <summary>Identificador estable de la operación, p. ej. <c>opencv.denoise.nlmeans</c>.</summary>
    public string Tipo { get; private set; }

    /// <summary>Versión exacta del motor/modelo (imagen Docker, pesos) para poder reproducirla.</summary>
    public string VersionMotor { get; private set; }

    /// <summary>Parámetros en JSON canónico, tal como se aplicaron.</summary>
    public string Parametros { get; private set; }

    public ViaProcesamiento Via { get; private set; }
    public HashSha256 HashSalida { get; private set; }
    public long TamanoSalidaBytes { get; private set; }
    public string Actor { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }

    public static OperacionForense Registrar(
        Caso caso,
        Evidencia entrada,
        string tipo,
        string versionMotor,
        string parametros,
        bool usaIa,
        HashSha256 hashSalida,
        long tamanoSalidaBytes,
        Actor actor,
        DateTimeOffset ahora)
    {
        if (entrada.CasoId != caso.Id)
            throw new ExcepcionDeDominio("La evidencia de entrada no pertenece al caso.");
        caso.ExigirOperacion(actor);
        if (string.IsNullOrWhiteSpace(tipo) || string.IsNullOrWhiteSpace(versionMotor))
            throw new ExcepcionDeDominio("Toda operación declara tipo y versión de motor.");
        Evidencia.ValidarContenido(hashSalida, tamanoSalidaBytes);

        return new OperacionForense
        {
            Id = Guid.NewGuid(),
            CasoId = caso.Id,
            EvidenciaEntradaId = entrada.Id,
            HashEntrada = entrada.Hash,
            Tipo = tipo,
            VersionMotor = versionMotor,
            Parametros = parametros ?? "{}",
            // Cualquier operación con IA saca al derivado de la vía judicial.
            Via = usaIa ? ViaProcesamiento.SoloInvestigacion : ViaProcesamiento.AdmisibleEnJuicio,
            HashSalida = hashSalida,
            TamanoSalidaBytes = tamanoSalidaBytes,
            Actor = actor.Usuario,
            RegistradaEn = Tiempo.NormalizarParaCustodia(ahora),
        };
    }
}
