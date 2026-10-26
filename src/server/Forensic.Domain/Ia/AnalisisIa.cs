using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Ia;

/// <summary>
/// Una ejecución de un modelo de IA sobre una evidencia y los hallazgos que reportó (también cero).
/// Corre en la workstation, así que el servidor no la puede re-verificar: registra su procedencia
/// (modelo, parámetros, entorno declarado, entrada) y la marca «sólo investigación». Inmutable.
/// </summary>
public sealed class AnalisisIa
{
    private readonly List<Hallazgo> _hallazgos = [];

    private AnalisisIa()
    {
        Tipo = null!;
        Modelo = null!;
        Parametros = null!;
        Entorno = null!;
        Actor = null!;
    }

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid EvidenciaId { get; private set; }

    /// <summary>Hash de la evidencia analizada: el que declaró el worker, verificado contra el custodiado.</summary>
    public HashSha256 HashEntrada { get; private set; }

    public string Tipo { get; private set; }
    public ModeloDeIa Modelo { get; private set; }
    public string Parametros { get; private set; }

    /// <summary>Entorno declarado por la workstation (runtime, proveedor, GPU), en JSON tal como lo reportó.</summary>
    public string Entorno { get; private set; }

    public string Actor { get; private set; }
    public DateTimeOffset RegistradoEn { get; private set; }
    public IReadOnlyList<Hallazgo> Hallazgos => _hallazgos;

    public ViaProcesamiento Via => ViaProcesamiento.SoloInvestigacion;

    public static AnalisisIa Registrar(
        Caso caso,
        Evidencia entrada,
        DefinicionDeAnalisisIa definicion,
        ModeloDeIa modelo,
        string entorno,
        HashSha256 hashEntradaDeclarado,
        IReadOnlyList<HallazgoPropuesto> hallazgos,
        Actor actor,
        DateTimeOffset ahora)
    {
        if (entrada.CasoId != caso.Id)
            throw new ExcepcionDeDominio("La evidencia analizada no pertenece al caso.");
        caso.ExigirOperacion(actor);
        ArgumentNullException.ThrowIfNull(definicion);
        ArgumentNullException.ThrowIfNull(hallazgos);
        CatalogoDeIa.ExigirModeloPermitido(definicion.Tipo, modelo);
        if (hashEntradaDeclarado != entrada.Hash)
            throw new ExcepcionDeDominio(
                $"El worker analizó otro contenido ({hashEntradaDeclarado}), no la evidencia {entrada.Id} ({entrada.Hash}).");
        if (!Json.EsObjeto(entorno))
            throw new ExcepcionDeDominio("Todo análisis de IA declara el entorno que lo ejecutó, como objeto JSON.");

        var analisis = new AnalisisIa
        {
            Id = Guid.NewGuid(),
            CasoId = caso.Id,
            EvidenciaId = entrada.Id,
            HashEntrada = entrada.Hash,
            Tipo = definicion.Tipo,
            Modelo = modelo,
            Parametros = definicion.ParametrosCanonicos,
            Entorno = entorno,
            Actor = actor.Usuario,
            RegistradoEn = Tiempo.NormalizarParaCustodia(ahora),
        };
        analisis._hallazgos.AddRange(hallazgos.Select((h, i) => Hallazgo.Desde(analisis.Id, i, h)));
        return analisis;
    }
}
