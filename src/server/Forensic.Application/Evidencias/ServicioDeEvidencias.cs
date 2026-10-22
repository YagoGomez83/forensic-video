using Forensic.Application.Custodia;
using Forensic.Domain.Casos;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;

namespace Forensic.Application.Evidencias;

public sealed record EvidenciaDto(
    Guid Id, Guid CasoId, string Hash, long TamanoBytes, string NombreOriginal, ViaProcesamiento Via,
    Guid? PadreId, DateTimeOffset IngestadaEn, string IngestadaPor)
{
    internal static EvidenciaDto Desde(Evidencia e) =>
        new(e.Id, e.CasoId, e.Hash.Hex, e.TamanoBytes, e.NombreOriginal, e.Via, e.PadreId, e.IngestadaEn, e.IngestadaPor);
}

public sealed record ContenidoDeEvidencia(EvidenciaDto Evidencia, Stream Contenido);

public sealed class ServicioDeEvidencias(
    IPersistenciaForense persistencia,
    IAlmacenDeEvidencia almacen,
    IWorkerDeIngesta worker,
    IActorActual actorActual,
    TimeProvider reloj)
{
    /// <summary>
    /// Ingesta de un original: primero se hashean los bytes crudos al escribirlos en el almacén (la
    /// fuente de verdad). Luego el worker analiza esos mismos bytes, releídos del almacén, y su hash
    /// tiene que coincidir; si no, la ingesta se rechaza. Sólo entonces se crea la evidencia y su
    /// asiento, ambos en la misma transacción. Si algo falla, la evidencia no existe; el blob queda
    /// huérfano pero es inofensivo (direccionado por contenido).
    /// </summary>
    public async Task<EvidenciaDto> IngestarAsync(Guid casoId, string nombreOriginal, Stream contenido, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        // Autorizar antes de aceptar bytes.
        caso.ExigirOperacion(actor);

        var almacenado = await almacen.GuardarAsync(contenido, ct);
        var analisis = await AnalizarAsync(almacenado, ct);

        // El reloj se lee después del análisis, que puede tardar: el asiento no puede quedar
        // fechado antes que otro asentado en la cadena mientras tanto.
        var ahora = reloj.GetUtcNow();
        var evidencia = Evidencia.Ingestar(caso, almacenado.Hash, almacenado.TamanoBytes, nombreOriginal, actor, ahora);
        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, ct);
        var asiento = RegistroDeCustodia.Asentar(
            ultimo, casoId, TipoAsiento.EvidenciaIngestada, evidencia.Id,
            DetalleDeAsiento.Serializar(new
            {
                sha256 = evidencia.Hash.Hex,
                evidencia.TamanoBytes,
                evidencia.NombreOriginal,
                via = evidencia.Via.ToString(),
                controlCruzado = new { sha256Worker = analisis.Hash.Hex, tamanoBytesWorker = analisis.TamanoBytes },
                metadatos = analisis.Metadatos,
                analisis.MotivoSinMetadatos,
            }),
            actor, ahora);

        persistencia.Agregar(evidencia);
        persistencia.Agregar(asiento);
        await persistencia.GuardarCambiosAsync(ct);
        return EvidenciaDto.Desde(evidencia);
    }

    public async Task<IReadOnlyList<EvidenciaDto>> ListarAsync(Guid casoId, CancellationToken ct)
    {
        var caso = await CargarCasoAsync(casoId, ct);
        caso.ExigirLectura(actorActual.Actor);
        var evidencias = await persistencia.ListarEvidenciasAsync(casoId, ct);
        return evidencias.Select(EvidenciaDto.Desde).ToList();
    }

    /// <summary>
    /// Entrega el contenido tras verificar su hash. El acceso queda asentado en la cadena antes
    /// de devolver el contenido.
    /// </summary>
    public async Task<ContenidoDeEvidencia> AbrirAsync(Guid casoId, Guid evidenciaId, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        caso.ExigirLectura(actor);
        var evidencia = await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, ct)
            ?? throw new NoEncontradoException($"No existe la evidencia {evidenciaId} en el caso.");

        var contenido = await almacen.AbrirVerificadoAsync(evidencia.Hash, ct);
        try
        {
            var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, ct);
            persistencia.Agregar(RegistroDeCustodia.Asentar(
                ultimo, casoId, TipoAsiento.EvidenciaAccedida, evidencia.Id,
                DetalleDeAsiento.Serializar(new { sha256 = evidencia.Hash.Hex, integridadVerificada = true }),
                actor, reloj.GetUtcNow()));
            await persistencia.GuardarCambiosAsync(ct);
        }
        catch
        {
            await contenido.DisposeAsync();
            throw;
        }
        return new ContenidoDeEvidencia(EvidenciaDto.Desde(evidencia), contenido);
    }

    /// <summary>
    /// Control cruzado: el worker recibe los bytes tal como quedaron en el almacén (releídos con
    /// verificación de hash) y su hash y tamaño tienen que coincidir con los del servidor.
    /// </summary>
    private async Task<AnalisisDeIngesta> AnalizarAsync(ContenidoAlmacenado almacenado, CancellationToken ct)
    {
        AnalisisDeIngesta analisis;
        await using (var crudo = await almacen.AbrirVerificadoAsync(almacenado.Hash, ct))
            analisis = await worker.AnalizarAsync(crudo, ct);

        if (analisis.Hash != almacenado.Hash || analisis.TamanoBytes != almacenado.TamanoBytes)
            throw new IngestaRechazadaException(
                $"El control cruzado no coincide: el servidor calculó {almacenado.Hash} ({almacenado.TamanoBytes} bytes) " +
                $"y el worker de ingesta {analisis.Hash} ({analisis.TamanoBytes} bytes).");
        return analisis;
    }

    private async Task<Caso> CargarCasoAsync(Guid casoId, CancellationToken ct) =>
        await persistencia.ObtenerCasoAsync(casoId, ct)
        ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
}
