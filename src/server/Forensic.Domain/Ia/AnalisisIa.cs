using System.Text;
using System.Text.Json;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Ia;

/// <summary>
/// Una ejecución de un modelo de IA sobre una evidencia y los hallazgos que reportó (también cero).
/// Corre en la workstation, así que el servidor no la puede re-verificar: registra su procedencia
/// (modelo, parámetros, entorno declarado, entrada) y la marca «sólo investigación». Inmutable.
/// </summary>
public sealed class AnalisisIa
{
    /// <summary>
    /// Formato del detalle de su asiento <see cref="TipoAsiento.HallazgosRegistrados"/>. Cambiar su
    /// estructura exige una versión nueva.
    /// </summary>
    public const string FormatoDeDetalle = "forensic-video/hallazgos-ia/v1";

    /// <summary>Lo que el servidor no puede verificar (entorno, ejecución) está declarado por la workstation.</summary>
    public const string OrigenDeLaProcedencia = "declarada-por-workstation";

    /// <summary>El reporte viene de la workstation: entrada no confiable, con topes.</summary>
    public const int MaximoHallazgos = 1000;

    public const int LongitudMaximaEntorno = 16384;

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
        ArgumentNullException.ThrowIfNull(entorno);
        CatalogoDeIa.ExigirModeloPermitido(definicion.Tipo, modelo);
        if (hashEntradaDeclarado != entrada.Hash)
            throw new ExcepcionDeDominio(
                $"El worker analizó otro contenido ({hashEntradaDeclarado}), no la evidencia {entrada.Id} ({entrada.Hash}).");
        var leeTexto = CatalogoDeIa.LeeTexto(definicion.Tipo);
        if (hallazgos.Any(h => (h.TextoLeido is not null) != leeTexto))
            throw new ExcepcionDeDominio(leeTexto
                ? $"Un análisis '{definicion.Tipo}' sólo produce texto leído, no clases."
                : $"Un análisis '{definicion.Tipo}' sólo produce clases de objetos, no texto leído.");
        if (hallazgos.Count > MaximoHallazgos)
            throw new ExcepcionDeDominio($"Un análisis admite hasta {MaximoHallazgos} hallazgos.");
        if (entorno.Length > LongitudMaximaEntorno)
            throw new ExcepcionDeDominio($"El entorno declarado admite hasta {LongitudMaximaEntorno} caracteres.");
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

    /// <summary>
    /// Detalle del asiento <see cref="TipoAsiento.HallazgosRegistrados"/>, en <see cref="FormatoDeDetalle"/>:
    /// la procedencia completa y cada hallazgo con su confianza y su dato crudo, byte a byte como los
    /// reportó el worker. El asiento solo alcanza para saber qué dijo el modelo y con qué se obtuvo.
    /// </summary>
    public string DetalleDeAsiento()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("formato", FormatoDeDetalle);
            w.WriteString("analisisId", Id);
            w.WriteString("analisis", Tipo);
            w.WritePropertyName("parametros");
            w.WriteRawValue(Parametros);
            w.WriteStartObject("modelo");
            w.WriteString("id", Modelo.Id);
            w.WriteString("version", Modelo.Version);
            w.WriteString("sha256", Modelo.Sha256.Hex);
            w.WriteEndObject();
            w.WriteStartObject("entrada");
            w.WriteString("evidenciaId", EvidenciaId);
            w.WriteString("sha256", HashEntrada.Hex);
            w.WriteEndObject();
            w.WriteStartObject("procedencia");
            w.WriteString("origen", OrigenDeLaProcedencia);
            w.WritePropertyName("entorno");
            w.WriteRawValue(Entorno);
            w.WriteEndObject();
            w.WriteStartArray("hallazgos");
            foreach (var h in _hallazgos)
            {
                w.WriteStartObject();
                w.WriteNumber("orden", h.Orden);
                w.WriteStartObject("region");
                w.WriteNumber("x", h.Region.X);
                w.WriteNumber("y", h.Region.Y);
                w.WriteNumber("ancho", h.Region.Ancho);
                w.WriteNumber("alto", h.Region.Alto);
                w.WriteEndObject();
                if (h.Clase is not null)
                    w.WriteString("clase", h.Clase);
                if (h.TextoLeido is not null)
                    w.WriteString("textoLeido", h.TextoLeido);
                w.WriteNumber("confianza", h.Confianza);
                w.WritePropertyName("crudo");
                w.WriteRawValue(h.DatoCrudo);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("via", Via.ToString());
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
