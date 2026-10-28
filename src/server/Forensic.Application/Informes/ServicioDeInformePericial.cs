using System.Text.Json;
using Forensic.Application.Casos;
using Forensic.Application.Custodia;
using Forensic.Domain.Casos;
using Forensic.Domain.Custodia;
using Forensic.Domain.Informes;
using Forensic.Domain.Operaciones;

namespace Forensic.Application.Informes;

/// <summary>
/// Emite el informe pericial: un PDF y un bundle verificable con sólo lo admisible en juicio. Lo que
/// entra lo decide <see cref="ContenidoPericial"/>, cuya única puerta para una evidencia es
/// <c>ExigirAdmisibleEnJuicio</c>; este servicio no tiene otro camino para agregar nada. El
/// investigativo es otro servicio, por otro endpoint, con otro tipo de bundle.
/// </summary>
public sealed class ServicioDeInformePericial(
    IPersistenciaForense persistencia,
    IAlmacenDeEvidencia almacen,
    VerificadorDeCadena verificador,
    ServicioDeSellado sellado,
    IAlmacenDeSellos almacenDeSellos,
    IFirmanteDeCadena firmante,
    IRedactorDeInformes redactor,
    IActorActual actorActual,
    TimeProvider reloj)
{
    public const string Tipo = "pericial";
    public const string FormatoDeDetalle = "forensic-video/informe/v1";

    /// <summary>
    /// Orden: autorizar; exigir la cadena íntegra y coincidente con sus sellos; sellar su cabeza N;
    /// armar el bundle (cadena hasta N) y el PDF; guardarlos en el almacén; asentar la emisión (N+1)
    /// con sus hashes. El bundle no puede contener su propio asiento de emisión. Si otro asiento llega
    /// antes que el de emisión, éste falla con conflicto (409) y no se asienta nada.
    /// </summary>
    /// <param name="umbralEmbebidoBytes">Por encima de este tamaño, una evidencia viaja aparte del bundle.</param>
    public async Task<InformeEmitidoDto> EmitirAsync(Guid casoId, long umbralEmbebidoBytes, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await persistencia.ObtenerCasoAsync(casoId, ct)
            ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
        caso.ExigirEmisionDeInformePericial(actor);

        var cadena = await verificador.ObtenerConfiableAsync(casoId, ct);
        var cabeza = cadena[^1];
        await sellado.SellarCabezaAsync(cabeza, ct);
        var sellos = (await almacenDeSellos.ListarAsync(casoId, ct)).Where(s => s.Secuencia <= cabeza.Secuencia).ToList();

        var contenido = ContenidoPericial.Armar(caso, await persistencia.ListarEvidenciasAsync(casoId, ct), cadena);
        var evidencias = contenido.Evidencias
            .Select(e => new EvidenciaDelBundle(e.Evidencia, e.AsientoDeOrigen,
                e.Evidencia.TamanoBytes <= umbralEmbebidoBytes ? BundleV1.RutaDe(e.Evidencia) : null))
            .ToList();
        var ahora = reloj.GetUtcNow();
        var emitidoEn = Domain.Comun.Tiempo.NormalizarParaCustodia(ahora);

        var pdf = redactor.RedactarPericial(ParaRedactar(caso, actor.Usuario, emitidoEn, cabeza, sellos[^1], contenido, evidencias));
        var archivos = new List<(string, byte[])>
        {
            (BundleV1.ArchivoCadena, BundleV1.Cadena(casoId, contenido.Asientos)),
            (BundleV1.ArchivoSellos, BundleV1.Sellos(sellos)),
            (BundleV1.ArchivoPdf, pdf),
            (BundleV1.ArchivoLeeme, BundleV1.Leeme(Tipo, caso, firmante.ClaveId, cabeza.Secuencia, evidencias)),
        };
        var manifiesto = BundleV1.Manifiesto(
            Tipo, caso, emitidoEn, actor.Usuario, firmante.ClaveId, firmante.ClavePublica, cabeza, archivos, evidencias);

        ContenidoAlmacenado bundle;
        await using (var temporal = new FileStream(
            Path.Combine(Path.GetTempPath(), $"forensic-bundle-{Guid.NewGuid():N}.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 1 << 16, FileOptions.DeleteOnClose | FileOptions.Asynchronous))
        {
            await BundleV1.EscribirAsync(
                temporal, manifiesto, firmante.Firmar(manifiesto), archivos, evidencias, emitidoEn, almacen.AbrirVerificadoAsync, ct);
            temporal.Position = 0;
            bundle = await almacen.GuardarAsync(temporal, ct);
        }
        var pdfGuardado = await almacen.GuardarAsync(new MemoryStream(pdf), ct);

        var detalle = DetalleDeAsiento.Serializar(new
        {
            formato = FormatoDeDetalle,
            tipo = Tipo,
            hasta = cabeza.Secuencia,
            hashCabeza = cabeza.Hash.Hex,
            sello = new { secuencia = sellos[^1].Secuencia, selladoEn = AsientoDeCustodia.TimestampCanonico(sellos[^1].SelladoEn) },
            pdf = new { sha256 = pdfGuardado.Hash.Hex, tamanoBytes = pdfGuardado.TamanoBytes },
            bundle = new { sha256 = bundle.Hash.Hex, tamanoBytes = bundle.TamanoBytes },
            evidencias = evidencias.Select(e => e.Evidencia.Id).ToList(),
            evidenciasExcluidas = contenido.EvidenciasExcluidas.Count,
            asientosOpacos = contenido.AsientosOpacos,
        });
        var asiento = RegistroDeCustodia.Asentar(cabeza, casoId, TipoAsiento.InformePericialEmitido, null, detalle, actor, ahora);
        persistencia.Agregar(asiento);
        await persistencia.GuardarCambiosAsync(ct);

        return ConsultaDeInformes.Desde(asiento)!;
    }

    private static InformePericialParaRedactar ParaRedactar(
        Caso caso, string emitidoPor, DateTimeOffset emitidoEn, AsientoDeCustodia cabeza, SelloDeCadena ultimoSello,
        ContenidoPericial contenido, IReadOnlyList<EvidenciaDelBundle> evidencias)
    {
        var porSecuencia = contenido.Asientos.ToDictionary(a => a.Asiento.Secuencia, a => a.Asiento);
        var redactadas = evidencias.Select(e =>
        {
            var ev = e.Evidencia;
            if (ev.EsOriginal)
                return new EvidenciaParaRedactar(ev.Id, ev.NombreOriginal, ev.Hash.Hex, ev.TamanoBytes, e.Embebida, e.AsientoDeOrigen,
                    null, null, null, null);
            var op = OperacionAsentada.Desde(porSecuencia[e.AsientoDeOrigen]);
            return new EvidenciaParaRedactar(ev.Id, ev.NombreOriginal, ev.Hash.Hex, ev.TamanoBytes, e.Embebida, e.AsientoDeOrigen,
                op.EvidenciaEntradaId, op.Tipo, op.Parametros, op.Entorno);
        }).ToList();
        var asientos = contenido.Asientos.Select(a => a.Opaco
            ? new AsientoParaRedactar(a.Asiento.Secuencia, true, null, null, null, null, a.Asiento.Hash.Hex)
            : new AsientoParaRedactar(a.Asiento.Secuencia, false, a.Asiento.Tipo.ToString(), a.Asiento.Actor, a.Asiento.Timestamp,
                a.Asiento.EvidenciaId, a.Asiento.Hash.Hex)).ToList();
        return new InformePericialParaRedactar(
            CasoDto.Desde(caso), emitidoPor, emitidoEn, ultimoSello.ClaveId, cabeza.Secuencia, cabeza.Hash.Hex, ultimoSello,
            redactadas, asientos, contenido.AsientosOpacos, contenido.EvidenciasExcluidas.Count);
    }
}

/// <summary>Los informes emitidos de un caso y sus archivos, leídos de la cadena y del almacén.</summary>
public sealed class ConsultaDeInformes(IPersistenciaForense persistencia, IAlmacenDeEvidencia almacen, IActorActual actorActual)
{
    public async Task<IReadOnlyList<InformeEmitidoDto>> ListarAsync(Guid casoId, CancellationToken ct)
    {
        await CasoLegibleAsync(casoId, ct);
        return (await persistencia.ObtenerCadenaAsync(casoId, ct)).Select(Desde).OfType<InformeEmitidoDto>().ToList();
    }

    /// <summary>
    /// El PDF o el bundle de un informe pericial, verificado contra el hash de su asiento. Sólo sirve
    /// asientos de emisión pericial: un informe de otro tipo no sale por este camino.
    /// </summary>
    public async Task<ArchivoDeInforme> AbrirPericialAsync(Guid casoId, long secuencia, bool bundle, CancellationToken ct)
    {
        var caso = await CasoLegibleAsync(casoId, ct);
        var asiento = (await persistencia.ObtenerCadenaAsync(casoId, ct))
            .SingleOrDefault(a => a.Secuencia == secuencia && a.Tipo == TipoAsiento.InformePericialEmitido)
            ?? throw new NoEncontradoException($"No hay un informe pericial en el asiento {secuencia} del caso.");
        var informe = Desde(asiento)!;
        var archivo = bundle ? informe.Bundle : informe.Pdf;
        var contenido = await almacen.AbrirVerificadoAsync(Domain.Comun.HashSha256.Parse(archivo.Sha256), ct);
        var nombre = $"informe-pericial-{caso.NumeroExpediente}-s{informe.Hasta}";
        return bundle
            ? new ArchivoDeInforme(contenido, "application/zip", nombre + ".zip")
            : new ArchivoDeInforme(contenido, "application/pdf", nombre + ".pdf");
    }

    private async Task<Caso> CasoLegibleAsync(Guid casoId, CancellationToken ct)
    {
        var caso = await persistencia.ObtenerCasoAsync(casoId, ct)
            ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
        caso.ExigirLectura(actorActual.Actor);
        return caso;
    }

    internal static InformeEmitidoDto? Desde(AsientoDeCustodia a)
    {
        if (a.Tipo != TipoAsiento.InformePericialEmitido)
            return null;
        using var doc = JsonDocument.Parse(a.Detalle);
        var d = doc.RootElement;
        ArchivoDelInformeDto Archivo(string nombre) =>
            new(d.GetProperty(nombre).GetProperty("sha256").GetString()!, d.GetProperty(nombre).GetProperty("tamanoBytes").GetInt64());
        return new InformeEmitidoDto(
            d.GetProperty("tipo").GetString()!, a.Secuencia, a.Timestamp, a.Actor, d.GetProperty("hasta").GetInt64(),
            Archivo("pdf"), Archivo("bundle"), d.GetProperty("evidencias").GetArrayLength(),
            d.GetProperty("evidenciasExcluidas").GetInt32(), d.GetProperty("asientosOpacos").GetInt32());
    }
}
