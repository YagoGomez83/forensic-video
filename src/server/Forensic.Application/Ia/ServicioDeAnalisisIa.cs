using System.Text.Json;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;
using Microsoft.Extensions.Logging;

namespace Forensic.Application.Ia;

public sealed record RegionDto(int X, int Y, int Ancho, int Alto);

public sealed record ModeloDeIaDto(string Id, string Version, string Sha256);

/// <param name="Crudo">El dato crudo del modelo, en JSON tal como lo reportó el worker.</param>
public sealed record HallazgoDto(
    int Orden, RegionDto Region, string? Clase, string? TextoLeido, decimal Confianza, string Crudo, ViaProcesamiento Via);

/// <param name="Parametros">JSON canónico, como se asentó.</param>
/// <param name="Entorno">Entorno declarado por la workstation, en JSON tal como lo reportó.</param>
public sealed record AnalisisIaDto(
    Guid Id, Guid EvidenciaId, string Analisis, string Parametros, ModeloDeIaDto Modelo, string Entorno, string Actor,
    DateTimeOffset RegistradoEn, ViaProcesamiento Via, IReadOnlyList<HallazgoDto> Hallazgos)
{
    public static AnalisisIaDto Desde(AnalisisIa a) =>
        new(a.Id, a.EvidenciaId, a.Tipo, a.Parametros, new ModeloDeIaDto(a.Modelo.Id, a.Modelo.Version, a.Modelo.Sha256.Hex),
            a.Entorno, a.Actor, a.RegistradoEn, a.Via,
            a.Hallazgos.Select(h => new HallazgoDto(
                h.Orden, new RegionDto(h.Region.X, h.Region.Y, h.Region.Ancho, h.Region.Alto), h.Clase, h.TextoLeido,
                h.Confianza, h.DatoCrudo, h.Via)).ToList());
}

/// <summary>
/// El resultado de un análisis de IA tal como lo devuelve el worker de la workstation (el cliente lo
/// reenvía sin tocarlo). Todo es anulable: viene de afuera y se valida campo por campo, siempre con 422.
/// </summary>
public sealed record ReporteDeAnalisisIa(
    string? Analisis, JsonElement Parametros, ModeloReportado? Modelo, EntradaReportada? Entrada, JsonElement Entorno,
    IReadOnlyList<HallazgoReportado?>? Hallazgos);

public sealed record ModeloReportado(string? Id, string? Version, string? Sha256);

public sealed record EntradaReportada(string? Sha256);

public sealed record HallazgoReportado(
    RegionDto? Region, string? Clase, string? TextoLeido, decimal? Confianza, JsonElement Crudo);

public sealed class ServicioDeAnalisisIa(
    IPersistenciaForense persistencia,
    IActorActual actorActual,
    TimeProvider reloj,
    ILogger<ServicioDeAnalisisIa> logger)
{
    /// <summary>
    /// Registra un análisis hecho en la workstation. El servidor no lo puede re-ejecutar: verifica lo que
    /// sí puede (la evidencia analizada es la custodiada, el modelo está en la lista blanca, permisos) y
    /// asienta todo como «sólo investigación». Análisis, hallazgos y asiento van en una sola transacción.
    /// </summary>
    public async Task<AnalisisIaDto> RegistrarAsync(
        Guid casoId, Guid evidenciaId, ReporteDeAnalisisIa reporte, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        caso.ExigirOperacion(actor);
        var evidencia = await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, ct)
            ?? throw new NoEncontradoException($"No existe la evidencia {evidenciaId} en el caso.");

        var definicion = CatalogoDeIa.Interpretar(Exigir(reporte.Analisis, "analisis"), reporte.Parametros);
        var modelo = Exigir(reporte.Modelo, "modelo");
        var modeloDeIa = new ModeloDeIa(
            Exigir(modelo.Id, "modelo.id"), Exigir(modelo.Version, "modelo.version"),
            HashSha256.Parse(Exigir(modelo.Sha256, "modelo.sha256")));
        var hashDeclarado = HashSha256.Parse(Exigir(Exigir(reporte.Entrada, "entrada").Sha256, "entrada.sha256"));
        if (hashDeclarado != evidencia.Hash)
            logger.LogWarning(
                "Reporte de IA sobre otro contenido: caso {CasoId}, evidencia {EvidenciaId} ({Hash}), declarado {Declarado}, actor {Actor}.",
                casoId, evidenciaId, evidencia.Hash.Hex, hashDeclarado.Hex, actor.Usuario);
        var hallazgos = Exigir(reporte.Hallazgos, "hallazgos").Select(Propuesto).ToList();

        var analisis = AnalisisIa.Registrar(
            caso, evidencia, definicion, modeloDeIa, ObjetoCrudo(reporte.Entorno, "entorno"), hashDeclarado, hallazgos,
            actor, reloj.GetUtcNow());
        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, ct);
        var asiento = RegistroDeCustodia.Asentar(
            ultimo, casoId, TipoAsiento.HallazgosRegistrados, evidencia.Id, analisis.DetalleDeAsiento(), actor,
            analisis.RegistradoEn);

        persistencia.Agregar(analisis);
        persistencia.Agregar(asiento);
        await persistencia.GuardarCambiosAsync(ct);
        return AnalisisIaDto.Desde(analisis);
    }

    /// <summary>Los análisis de IA de una evidencia. Es metadato, como el linaje: no se asienta.</summary>
    public async Task<IReadOnlyList<AnalisisIaDto>> ListarAsync(Guid casoId, Guid evidenciaId, CancellationToken ct)
    {
        var caso = await CargarCasoAsync(casoId, ct);
        caso.ExigirLectura(actorActual.Actor);
        _ = await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, ct)
            ?? throw new NoEncontradoException($"No existe la evidencia {evidenciaId} en el caso.");
        return (await persistencia.ListarAnalisisIaAsync(casoId, evidenciaId, ct)).Select(AnalisisIaDto.Desde).ToList();
    }

    private static HallazgoPropuesto Propuesto(HallazgoReportado? h, int i)
    {
        var hallazgo = Exigir(h, $"hallazgos[{i}]");
        var r = Exigir(hallazgo.Region, $"hallazgos[{i}].region");
        var region = RegionDeImagen.Crear(r.X, r.Y, r.Ancho, r.Alto);
        var confianza = Exigir(hallazgo.Confianza, $"hallazgos[{i}].confianza");
        var crudo = ObjetoCrudo(hallazgo.Crudo, $"hallazgos[{i}].crudo");
        return (hallazgo.Clase, hallazgo.TextoLeido) switch
        {
            ({ } clase, null) => HallazgoPropuesto.Deteccion(region, clase, confianza, crudo),
            (null, { } texto) => HallazgoPropuesto.Texto(region, texto, confianza, crudo),
            _ => throw new ExcepcionDeDominio($"hallazgos[{i}] lleva 'clase' o 'textoLeido', uno de los dos."),
        };
    }

    /// <summary>El texto exacto del objeto, como vino en el reporte: no se re-serializa.</summary>
    private static string ObjetoCrudo(JsonElement valor, string campo) =>
        valor.ValueKind == JsonValueKind.Object
            ? valor.GetRawText()
            : throw new ExcepcionDeDominio($"El reporte requiere '{campo}' como objeto JSON.");

    private static T Exigir<T>(T? valor, string campo) where T : class =>
        valor ?? throw new ExcepcionDeDominio($"Al reporte le falta '{campo}'.");

    private static T Exigir<T>(T? valor, string campo) where T : struct =>
        valor ?? throw new ExcepcionDeDominio($"Al reporte le falta '{campo}'.");

    private async Task<Caso> CargarCasoAsync(Guid casoId, CancellationToken ct) =>
        await persistencia.ObtenerCasoAsync(casoId, ct)
        ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
}
