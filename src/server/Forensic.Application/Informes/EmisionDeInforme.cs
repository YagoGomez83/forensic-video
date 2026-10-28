using Forensic.Application.Custodia;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Informes;

namespace Forensic.Application.Informes;

/// <summary>El contenido ya decidido de un informe, listo para empaquetar.</summary>
internal sealed record ContenidoAEmitir(
    string Tipo, TipoAsiento TipoDeAsiento, IReadOnlyList<AsientoDelInforme> Asientos, IReadOnlyList<EvidenciaDelInforme> Evidencias,
    int EvidenciasExcluidas, int AsientosOpacos);

/// <summary>
/// La mecánica común de emitir un informe sobre una cadena ya verificada: sellar su cabeza N, escribir
/// el bundle hasta N, guardar PDF y bundle y asentar la emisión (N+1). No decide qué contiene el
/// informe: eso llega ya resuelto por <see cref="ContenidoPericial"/> o <see cref="ContenidoInvestigativo"/>,
/// cada uno desde su servicio y su endpoint.
/// </summary>
internal static class EmisionDeInforme
{
    public const string FormatoDeDetalle = "forensic-video/informe/v1";

    public sealed record Preparado(
        AsientoDeCustodia Cabeza, SelloDeCadena UltimoSello, IReadOnlyList<SelloDeCadena> Sellos,
        IReadOnlyList<EvidenciaDelBundle> Evidencias, DateTimeOffset EmitidoEn);

    public static async Task<Preparado> PrepararAsync(
        IReadOnlyList<AsientoDeCustodia> cadena, IReadOnlyList<EvidenciaDelInforme> evidencias, long umbralEmbebidoBytes,
        ServicioDeSellado sellado, IAlmacenDeSellos almacenDeSellos, TimeProvider reloj, CancellationToken ct)
    {
        var cabeza = cadena[^1];
        await sellado.SellarCabezaAsync(cabeza, ct);
        var sellos = (await almacenDeSellos.ListarAsync(cabeza.CasoId, ct)).Where(s => s.Secuencia <= cabeza.Secuencia).ToList();
        var enElBundle = evidencias
            .Select(e => new EvidenciaDelBundle(e.Evidencia, e.AsientoDeOrigen,
                e.Evidencia.TamanoBytes <= umbralEmbebidoBytes ? BundleV1.RutaDe(e.Evidencia) : null))
            .ToList();
        return new Preparado(cabeza, sellos[^1], sellos, enElBundle, Tiempo.NormalizarParaCustodia(reloj.GetUtcNow()));
    }

    public static async Task<InformeEmitidoDto> EmitirAsync(
        Caso caso, Actor actor, ContenidoAEmitir contenido, Preparado p, byte[] pdf,
        IPersistenciaForense persistencia, IAlmacenDeEvidencia almacen, IFirmanteDeCadena firmante, CancellationToken ct)
    {
        var archivos = new List<(string, byte[])>
        {
            (BundleV1.ArchivoCadena, BundleV1.Cadena(caso.Id, contenido.Asientos)),
            (BundleV1.ArchivoSellos, BundleV1.Sellos(p.Sellos)),
            (BundleV1.ArchivoPdf, pdf),
            (BundleV1.ArchivoLeeme, BundleV1.Leeme(contenido.Tipo, caso, firmante.ClaveId, p.Cabeza.Secuencia, p.Evidencias)),
        };
        var manifiesto = BundleV1.Manifiesto(
            contenido.Tipo, caso, p.EmitidoEn, actor.Usuario, firmante.ClaveId, firmante.ClavePublica, p.Cabeza, archivos, p.Evidencias);

        ContenidoAlmacenado bundle;
        await using (var temporal = new FileStream(
            Path.Combine(Path.GetTempPath(), $"forensic-bundle-{Guid.NewGuid():N}.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 1 << 16, FileOptions.DeleteOnClose | FileOptions.Asynchronous))
        {
            await BundleV1.EscribirAsync(
                temporal, manifiesto, firmante.Firmar(manifiesto), archivos, p.Evidencias, p.EmitidoEn, almacen.AbrirVerificadoAsync, ct);
            temporal.Position = 0;
            bundle = await almacen.GuardarAsync(temporal, ct);
        }
        var pdfGuardado = await almacen.GuardarAsync(new MemoryStream(pdf), ct);

        var detalle = DetalleDeAsiento.Serializar(new
        {
            formato = FormatoDeDetalle,
            tipo = contenido.Tipo,
            hasta = p.Cabeza.Secuencia,
            hashCabeza = p.Cabeza.Hash.Hex,
            sello = new { secuencia = p.UltimoSello.Secuencia, selladoEn = AsientoDeCustodia.TimestampCanonico(p.UltimoSello.SelladoEn) },
            pdf = new { sha256 = pdfGuardado.Hash.Hex, tamanoBytes = pdfGuardado.TamanoBytes },
            bundle = new { sha256 = bundle.Hash.Hex, tamanoBytes = bundle.TamanoBytes },
            evidencias = p.Evidencias.Select(e => e.Evidencia.Id).ToList(),
            evidenciasExcluidas = contenido.EvidenciasExcluidas,
            asientosOpacos = contenido.AsientosOpacos,
        });
        var asiento = RegistroDeCustodia.Asentar(p.Cabeza, caso.Id, contenido.TipoDeAsiento, null, detalle, actor, p.EmitidoEn);
        persistencia.Agregar(asiento);
        await persistencia.GuardarCambiosAsync(ct);
        return ConsultaDeInformes.Desde(asiento)!;
    }

    public static AsientoParaRedactar ParaRedactar(AsientoDelInforme a) => a.Opaco
        ? new AsientoParaRedactar(a.Asiento.Secuencia, true, null, null, null, null, a.Asiento.Hash.Hex)
        : new AsientoParaRedactar(a.Asiento.Secuencia, false, a.Asiento.Tipo.ToString(), a.Asiento.Actor, a.Asiento.Timestamp,
            a.Asiento.EvidenciaId, a.Asiento.Hash.Hex);
}
