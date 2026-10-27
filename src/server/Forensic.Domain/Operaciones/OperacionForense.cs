using System.Text;
using System.Text.Json;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;

namespace Forensic.Domain.Operaciones;

/// <summary>
/// Un paso de procesamiento aplicado a una evidencia: qué se aplicó (tipo y parámetros canónicos),
/// sobre qué entrada, en qué entorno y qué salió. Es todo lo que hace falta para reproducirla.
/// Inmutable una vez registrada.
/// </summary>
public sealed class OperacionForense
{
    /// <summary>
    /// Formato del detalle de su asiento. Cambiar su estructura exige una versión nueva: los asientos
    /// existentes se siguen leyendo con la suya (<see cref="OperacionAsentada"/>).
    /// </summary>
    public const string FormatoDeDetalle = "forensic-video/operacion/v1";

    /// <summary>
    /// Formato del detalle de una operación de IA (<see cref="Custodia.TipoAsiento.OperacionIaAplicada"/>):
    /// procedencia (modelo, entorno declarado), no reproducción. <see cref="OperacionAsentada"/> no lo lee.
    /// </summary>
    public const string FormatoDeDetalleIa = "forensic-video/operacion-ia/v1";

    /// <summary>Todo derivado de la Fase 2 es PNG lossless, codificado de forma determinista.</summary>
    public const string FormatoDeSalida = "png";

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

    // El modelo de una operación de IA, en columnas propias (null en una determinista).
    public string? ModeloId { get; private set; }
    public string? ModeloVersion { get; private set; }
    public HashSha256? ModeloSha256 { get; private set; }

    /// <summary>El modelo que la ejecutó, si es una operación de IA.</summary>
    public ModeloDeIa? Modelo =>
        ModeloId is null ? null : new ModeloDeIa(ModeloId, ModeloVersion!, ModeloSha256!.Value);

    /// <summary>Una operación determinista del catálogo: el derivado sigue la vía de su entrada.</summary>
    public static OperacionForense Registrar(
        Caso caso,
        Evidencia entrada,
        OperacionDeterminista operacion,
        string entorno,
        HashSha256 hashSalida,
        long tamanoSalidaBytes,
        string? hashPixelesSalida,
        Actor actor,
        DateTimeOffset ahora) =>
        Crear(caso, entrada, operacion, entorno, hashSalida, tamanoSalidaBytes, hashPixelesSalida, actor, ahora);

    /// <summary>
    /// Una operación de IA, ejecutada en la workstation con un modelo de la lista blanca. El servidor no
    /// la puede re-ejecutar: queda su procedencia, y el derivado es «sólo investigación».
    /// </summary>
    public static OperacionForense RegistrarIa(
        Caso caso,
        Evidencia entrada,
        OperacionDeIa operacion,
        ModeloDeIa modelo,
        string entorno,
        HashSha256 hashSalida,
        long tamanoSalidaBytes,
        Actor actor,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        ArgumentNullException.ThrowIfNull(entorno);
        CatalogoDeIa.ExigirModeloPermitido(operacion.Tipo, modelo);
        // El entorno lo declara la workstation: entrada no confiable, con el mismo tope que en un análisis.
        if (entorno.Length > AnalisisIa.LongitudMaximaEntorno)
            throw new ExcepcionDeDominio($"El entorno declarado admite hasta {AnalisisIa.LongitudMaximaEntorno} caracteres.");
        var registrada = Crear(caso, entrada, operacion, entorno, hashSalida, tamanoSalidaBytes, null, actor, ahora);
        (registrada.ModeloId, registrada.ModeloVersion, registrada.ModeloSha256) = (modelo.Id, modelo.Version, modelo.Sha256);
        return registrada;
    }

    private static OperacionForense Crear(
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
        if (!Json.EsObjeto(entorno))
            throw new ExcepcionDeDominio(
                "Toda operación declara el entorno que la ejecutó, como objeto JSON: sin él no es reproducible.");
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

    /// <summary>
    /// Detalle del asiento <see cref="Custodia.TipoAsiento.OperacionAplicada"/>, en
    /// <see cref="FormatoDeDetalle"/>. Parámetros y entorno van como JSON crudo, byte a byte como se
    /// aplicaron, para que el asiento solo alcance para reproducir la operación.
    /// </summary>
    public string DetalleDeAsiento(Evidencia derivado)
    {
        if (derivado.OperacionOrigenId != Id)
            throw new ExcepcionDeDominio("El derivado no salió de esta operación.");
        if (Modelo is { } modelo)
            return DetalleDeAsientoIa(derivado, modelo);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("formato", FormatoDeDetalle);
            w.WriteString("operacion", Tipo);
            w.WritePropertyName("parametros");
            w.WriteRawValue(Parametros);
            w.WriteStartObject("entrada");
            w.WriteString("evidenciaId", EvidenciaEntradaId);
            w.WriteString("sha256", HashEntrada.Hex);
            w.WriteEndObject();
            w.WriteStartObject("salida");
            w.WriteString("evidenciaId", derivado.Id);
            w.WriteString("sha256", HashSalida.Hex);
            w.WriteNumber("tamanoBytes", TamanoSalidaBytes);
            w.WriteString("formato", FormatoDeSalida);
            w.WriteString("sha256Pixeles", HashPixelesSalida);
            w.WriteEndObject();
            w.WritePropertyName("entorno");
            w.WriteRawValue(Entorno);
            w.WriteString("via", Via.ToString());
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Detalle del asiento <see cref="Custodia.TipoAsiento.OperacionIaAplicada"/>, en
    /// <see cref="FormatoDeDetalleIa"/>: qué se aplicó, con qué modelo, sobre qué entrada y qué salió.
    /// El entorno lo declara la workstation; el hash de la salida lo calculó el servidor.
    /// </summary>
    private string DetalleDeAsientoIa(Evidencia derivado, ModeloDeIa modelo)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("formato", FormatoDeDetalleIa);
            w.WriteString("operacion", Tipo);
            w.WritePropertyName("parametros");
            w.WriteRawValue(Parametros);
            w.WriteStartObject("modelo");
            w.WriteString("id", modelo.Id);
            w.WriteString("version", modelo.Version);
            w.WriteString("sha256", modelo.Sha256.Hex);
            w.WriteEndObject();
            w.WriteStartObject("entrada");
            w.WriteString("evidenciaId", EvidenciaEntradaId);
            w.WriteString("sha256", HashEntrada.Hex);
            w.WriteEndObject();
            w.WriteStartObject("salida");
            w.WriteString("evidenciaId", derivado.Id);
            w.WriteString("sha256", HashSalida.Hex);
            w.WriteNumber("tamanoBytes", TamanoSalidaBytes);
            w.WriteString("formato", FormatoDeSalida);
            w.WriteEndObject();
            w.WriteStartObject("procedencia");
            w.WriteString("origen", AnalisisIa.OrigenDeLaProcedencia);
            w.WritePropertyName("entorno");
            w.WriteRawValue(Entorno);
            w.WriteEndObject();
            w.WriteString("via", Via.ToString());
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
