using ForensicVideo.Application.Custodia;
using ForensicVideo.Domain.Casos;
using ForensicVideo.Domain.Custodia;
using ForensicVideo.Domain.Evidencias;

namespace ForensicVideo.Application.Evidencias;

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
    IActorActual actorActual,
    TimeProvider reloj)
{
    /// <summary>
    /// Ingesta de un original: primero se hashea (al escribirlo en el almacén) y sólo entonces se
    /// crea la evidencia y su asiento, ambos en la misma transacción. Si el asiento falla, la
    /// evidencia no existe; el blob queda huérfano pero es inofensivo (direccionado por contenido).
    /// </summary>
    public async Task<EvidenciaDto> IngestarAsync(Guid casoId, string nombreOriginal, Stream contenido, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        // Autorizar antes de aceptar bytes.
        caso.ExigirOperacion(actor);

        var almacenado = await almacen.GuardarAsync(contenido, ct);

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

    private async Task<Caso> CargarCasoAsync(Guid casoId, CancellationToken ct) =>
        await persistencia.ObtenerCasoAsync(casoId, ct)
        ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
}
