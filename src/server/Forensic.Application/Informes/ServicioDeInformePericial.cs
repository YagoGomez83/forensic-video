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
/// investigativo es otro servicio (<see cref="ServicioDeInformeInvestigativo"/>), por otro endpoint,
/// con otro tipo de bundle.
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
        var contenido = ContenidoPericial.Armar(caso, await persistencia.ListarEvidenciasAsync(casoId, ct), cadena);
        var p = await EmisionDeInforme.PrepararAsync(cadena, contenido.Evidencias, umbralEmbebidoBytes, sellado, almacenDeSellos, reloj, ct);

        var pdf = redactor.RedactarPericial(new InformePericialParaRedactar(
            CasoDto.Desde(caso), actor.Usuario, p.EmitidoEn, p.UltimoSello.ClaveId, p.Cabeza.Secuencia, p.Cabeza.Hash.Hex, p.UltimoSello,
            p.Evidencias.Select(e => Redactar(e, cadena)).ToList(),
            contenido.Asientos.Select(EmisionDeInforme.ParaRedactar).ToList(),
            contenido.AsientosOpacos, contenido.EvidenciasExcluidas.Count));

        return await EmisionDeInforme.EmitirAsync(
            caso, actor,
            new ContenidoAEmitir(Tipo, TipoAsiento.InformePericialEmitido, contenido.Asientos, contenido.Evidencias,
                contenido.EvidenciasExcluidas.Count, contenido.AsientosOpacos),
            p, pdf, persistencia, almacen, firmante, ct);
    }

    private static EvidenciaParaRedactar Redactar(EvidenciaDelBundle e, IReadOnlyList<AsientoDeCustodia> cadena)
    {
        var ev = e.Evidencia;
        if (ev.EsOriginal)
            return new EvidenciaParaRedactar(ev.Id, ev.NombreOriginal, ev.Hash.Hex, ev.TamanoBytes, e.Embebida, e.AsientoDeOrigen,
                null, null, null, null);
        var op = OperacionAsentada.Desde(cadena[(int)e.AsientoDeOrigen - 1]);
        return new EvidenciaParaRedactar(ev.Id, ev.NombreOriginal, ev.Hash.Hex, ev.TamanoBytes, e.Embebida, e.AsientoDeOrigen,
            op.EvidenciaEntradaId, op.Tipo, op.Parametros, op.Entorno);
    }
}

/// <summary>
/// Emite el informe investigativo: todo el caso, con la IA, marcado «sólo investigación». Lo que entra
/// lo decide <see cref="ContenidoInvestigativo"/>. Es un camino aparte del pericial: otro servicio,
/// otro endpoint, otro tipo de bundle y otro asiento; nada de lo que produce puede salir como pericial.
/// </summary>
public sealed class ServicioDeInformeInvestigativo(
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
    public const string Tipo = "investigativo";

    public async Task<InformeEmitidoDto> EmitirAsync(Guid casoId, long umbralEmbebidoBytes, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await persistencia.ObtenerCasoAsync(casoId, ct)
            ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
        caso.ExigirEmisionDeInformeInvestigativo(actor);

        var cadena = await verificador.ObtenerConfiableAsync(casoId, ct);
        var contenido = ContenidoInvestigativo.Armar(caso, await persistencia.ListarEvidenciasAsync(casoId, ct), cadena);
        var p = await EmisionDeInforme.PrepararAsync(cadena, contenido.Evidencias, umbralEmbebidoBytes, sellado, almacenDeSellos, reloj, ct);

        var analisis = new List<AnalisisParaRedactar>();
        foreach (var e in contenido.Evidencias)
        {
            foreach (var a in await persistencia.ListarAnalisisIaAsync(casoId, e.Evidencia.Id, ct))
                analisis.Add(new AnalisisParaRedactar(
                    a.EvidenciaId, a.Tipo, $"{a.Modelo.Id} {a.Modelo.Version} (sha256 {a.Modelo.Sha256.Hex})", a.Actor, a.RegistradoEn,
                    a.Hallazgos.Select(DescribirHallazgo).ToList()));
        }

        var pdf = redactor.RedactarInvestigativo(new InformeInvestigativoParaRedactar(
            CasoDto.Desde(caso), actor.Usuario, p.EmitidoEn, p.UltimoSello.ClaveId, p.Cabeza.Secuencia, p.Cabeza.Hash.Hex, p.UltimoSello,
            p.Evidencias.Select(e => Redactar(e, cadena)).ToList(), analisis,
            contenido.Asientos.Select(EmisionDeInforme.ParaRedactar).ToList()));

        return await EmisionDeInforme.EmitirAsync(
            caso, actor,
            new ContenidoAEmitir(Tipo, TipoAsiento.InformeInvestigativoEmitido, contenido.Asientos, contenido.Evidencias, 0, 0),
            p, pdf, persistencia, almacen, firmante, ct);
    }

    /// <summary>Lo que dijo el modelo, nunca como un hecho: «el modelo leyó «ABC123» con 0,82».</summary>
    internal static string DescribirHallazgo(Domain.Ia.Hallazgo h)
    {
        var confianza = h.Confianza.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        var r = h.Region;
        var donde = $"en x={r.X}, y={r.Y}, {r.Ancho}×{r.Alto} px";
        return h.TextoLeido is { } texto
            ? $"el modelo leyó «{texto}» con {confianza} {donde}"
            : $"el modelo detectó «{h.Clase}» con {confianza} {donde}";
    }

    private static EvidenciaParaRedactar Redactar(EvidenciaDelBundle e, IReadOnlyList<AsientoDeCustodia> cadena)
    {
        var ev = e.Evidencia;
        var soloInvestigacion = ev.Via == Domain.Evidencias.ViaProcesamiento.SoloInvestigacion;
        if (ev.EsOriginal)
            return new EvidenciaParaRedactar(ev.Id, ev.NombreOriginal, ev.Hash.Hex, ev.TamanoBytes, e.Embebida, e.AsientoDeOrigen,
                null, null, null, null, soloInvestigacion);

        // Operaciones OpenCV e IA comparten operacion, parametros y entrada; la de IA trae modelo y procedencia.
        using var doc = JsonDocument.Parse(cadena[(int)e.AsientoDeOrigen - 1].Detalle);
        var d = doc.RootElement;
        var entorno = d.TryGetProperty("procedencia", out var procedencia) ? procedencia.GetProperty("entorno") : d.GetProperty("entorno");
        var modelo = d.TryGetProperty("modelo", out var m)
            ? $"{m.GetProperty("id").GetString()} {m.GetProperty("version").GetString()} (sha256 {m.GetProperty("sha256").GetString()})"
            : null;
        return new EvidenciaParaRedactar(ev.Id, ev.NombreOriginal, ev.Hash.Hex, ev.TamanoBytes, e.Embebida, e.AsientoDeOrigen,
            d.GetProperty("entrada").GetProperty("evidenciaId").GetGuid(), d.GetProperty("operacion").GetString(),
            d.GetProperty("parametros").GetRawText(), entorno.GetRawText(), soloInvestigacion, modelo);
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
    /// El PDF o el bundle de un informe, verificado contra el hash de su asiento. Cada tipo sale sólo por
    /// su propia ruta: pedir un investigativo por la del pericial (o al revés) es un 404.
    /// </summary>
    public async Task<ArchivoDeInforme> AbrirAsync(Guid casoId, string tipo, long secuencia, bool bundle, CancellationToken ct)
    {
        var tipoDeAsiento = tipo switch
        {
            ServicioDeInformePericial.Tipo => TipoAsiento.InformePericialEmitido,
            ServicioDeInformeInvestigativo.Tipo => TipoAsiento.InformeInvestigativoEmitido,
            _ => throw new NoEncontradoException($"No existe el tipo de informe {tipo}."),
        };
        var caso = await CasoLegibleAsync(casoId, ct);
        var asiento = (await persistencia.ObtenerCadenaAsync(casoId, ct))
            .SingleOrDefault(a => a.Secuencia == secuencia && a.Tipo == tipoDeAsiento)
            ?? throw new NoEncontradoException($"No hay un informe {tipo} en el asiento {secuencia} del caso.");
        var informe = Desde(asiento)!;
        var archivo = bundle ? informe.Bundle : informe.Pdf;
        var contenido = await almacen.AbrirVerificadoAsync(Domain.Comun.HashSha256.Parse(archivo.Sha256), ct);
        var nombre = $"informe-{tipo}-{caso.NumeroExpediente}-s{informe.Hasta}";
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
        if (a.Tipo is not (TipoAsiento.InformePericialEmitido or TipoAsiento.InformeInvestigativoEmitido))
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
