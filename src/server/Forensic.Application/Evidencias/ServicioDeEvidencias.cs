using System.Text.Json;
using Forensic.Application.Custodia;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Microsoft.Extensions.Logging;

namespace Forensic.Application.Evidencias;

public sealed record EvidenciaDto(
    Guid Id, Guid CasoId, string Hash, long TamanoBytes, string NombreOriginal, ViaProcesamiento Via,
    Guid? PadreId, DateTimeOffset IngestadaEn, string IngestadaPor)
{
    internal static EvidenciaDto Desde(Evidencia e) =>
        new(e.Id, e.CasoId, e.Hash.Hex, e.TamanoBytes, e.NombreOriginal, e.Via, e.PadreId, e.IngestadaEn, e.IngestadaPor);
}

public sealed record ContenidoDeEvidencia(EvidenciaDto Evidencia, Stream Contenido);

/// <param name="Resultado"><c>coincide</c> o <c>divergente</c>.</param>
public sealed record ResultadoDeControlCruzado(Guid EvidenciaId, string Resultado, string Sha256Worker);

public sealed class ServicioDeEvidencias(
    IPersistenciaForense persistencia,
    IAlmacenDeEvidencia almacen,
    IWorkerDeIngesta worker,
    IActorActual actorActual,
    TimeProvider reloj,
    ILogger<ServicioDeEvidencias> logger)
{
    private const string Pendiente = "pendiente";
    private const string WorkerNoDisponible = "worker de ingesta no disponible";

    /// <summary>
    /// Ingesta de un original: primero se hashean los bytes crudos al escribirlos en el almacén (la
    /// fuente de verdad). Luego el worker analiza esos mismos bytes, releídos del almacén, y su hash
    /// tiene que coincidir: si discrepa, la ingesta no ocurre (evento de seguridad). Si el worker no
    /// responde, la ingesta procede con el control cruzado pendiente, a completar con
    /// <see cref="CompletarControlCruzadoAsync"/>. La evidencia y su asiento se guardan en una sola
    /// transacción; si algo falla, el blob queda huérfano pero es inofensivo (direccionado por contenido).
    /// </summary>
    public async Task<EvidenciaDto> IngestarAsync(Guid casoId, string nombreOriginal, Stream contenido, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        // Autorizar antes de aceptar bytes.
        caso.ExigirOperacion(actor);

        var almacenado = await almacen.GuardarAsync(contenido, ct);
        AnalisisDeIngesta? analisis = null;
        try
        {
            analisis = await AnalizarAsync(almacenado.Hash, ct);
        }
        catch (WorkerDeIngestaNoDisponibleException ex)
        {
            // La integridad ya la garantiza el almacén: sin worker sólo faltan el cruce y los metadatos.
            logger.LogWarning(ex, "Ingesta en el caso {CasoId} con control cruzado pendiente ({Hash}).", casoId, almacenado.Hash.Hex);
        }
        if (analisis is not null && !Coincide(analisis, almacenado.Hash, almacenado.TamanoBytes))
            throw Divergencia(casoId, null, almacenado.Hash, almacenado.TamanoBytes, analisis);

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
                controlCruzado = analisis is null
                    ? (object)new { estado = Pendiente }
                    : new { estado = "coincide", sha256Worker = analisis.Hash.Hex, tamanoBytesWorker = analisis.TamanoBytes },
                metadatos = analisis?.Metadatos,
                motivoSinMetadatos = analisis is null ? WorkerNoDisponible : analisis.MotivoSinMetadatos,
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
    /// Completa un control cruzado que quedó pendiente porque el worker no respondió al ingestar.
    /// El resultado va en un asiento nuevo (el de ingesta es inmutable). Si el worker discrepa, la
    /// divergencia se asienta igual y se lanza <see cref="DivergenciaDeControlCruzadoException"/>:
    /// la evidencia no se toca, porque su integridad la garantiza el almacén.
    /// </summary>
    public async Task<ResultadoDeControlCruzado> CompletarControlCruzadoAsync(Guid casoId, Guid evidenciaId, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        caso.ExigirOperacion(actor);
        var evidencia = await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, ct)
            ?? throw new NoEncontradoException($"No existe la evidencia {evidenciaId} en el caso.");

        var cadena = await persistencia.ObtenerCadenaAsync(casoId, ct);
        if (!ControlCruzadoPendiente(cadena, evidencia.Id))
            throw new ExcepcionDeDominio($"La evidencia {evidenciaId} no tiene un control cruzado pendiente.");

        var analisis = await AnalizarAsync(evidencia.Hash, ct);
        var coincide = Coincide(analisis, evidencia.Hash, evidencia.TamanoBytes);
        var resultado = coincide ? "coincide" : "divergente";

        var ahora = reloj.GetUtcNow();
        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, ct);
        persistencia.Agregar(RegistroDeCustodia.Asentar(
            ultimo, casoId, TipoAsiento.ControlCruzadoCompletado, evidencia.Id,
            DetalleDeAsiento.Serializar(new
            {
                resultado,
                sha256 = evidencia.Hash.Hex,
                evidencia.TamanoBytes,
                sha256Worker = analisis.Hash.Hex,
                tamanoBytesWorker = analisis.TamanoBytes,
                metadatos = coincide ? analisis.Metadatos : null,
                motivoSinMetadatos = coincide ? analisis.MotivoSinMetadatos : "control cruzado divergente",
            }),
            actor, ahora));
        await persistencia.GuardarCambiosAsync(ct);

        if (!coincide)
            throw Divergencia(casoId, evidencia.Id, evidencia.Hash, evidencia.TamanoBytes, analisis);
        return new ResultadoDeControlCruzado(evidencia.Id, resultado, analisis.Hash.Hex);
    }

    /// <summary>El worker recibe los bytes tal como están en el almacén, releídos con verificación de hash.</summary>
    private async Task<AnalisisDeIngesta> AnalizarAsync(HashSha256 hash, CancellationToken ct)
    {
        await using var crudo = await almacen.AbrirVerificadoAsync(hash, ct);
        return await worker.AnalizarAsync(crudo, ct);
    }

    private static bool Coincide(AnalisisDeIngesta analisis, HashSha256 hash, long tamanoBytes) =>
        analisis.Hash == hash && analisis.TamanoBytes == tamanoBytes;

    /// <summary>
    /// Los bytes que recibió el worker ya estaban verificados contra el almacén: si reporta otra cosa,
    /// hubo corrupción en tránsito o el worker está roto o comprometido.
    /// </summary>
    private DivergenciaDeControlCruzadoException Divergencia(
        Guid casoId, Guid? evidenciaId, HashSha256 hash, long tamanoBytes, AnalisisDeIngesta analisis)
    {
        logger.LogCritical(
            "Divergencia en el control cruzado del worker de ingesta: caso {CasoId}, evidencia {EvidenciaId}. " +
            "Servidor {Hash} ({TamanoBytes} bytes), worker {HashWorker} ({TamanoBytesWorker} bytes).",
            casoId, evidenciaId, hash.Hex, tamanoBytes, analisis.Hash.Hex, analisis.TamanoBytes);
        return new DivergenciaDeControlCruzadoException(
            $"El worker de ingesta reportó {analisis.Hash} ({analisis.TamanoBytes} bytes) para bytes verificados " +
            $"{hash} ({tamanoBytes} bytes).");
    }

    /// <summary>
    /// Las tablas de custodia son inmutables, así que el estado del cruce se deduce de la cadena: el
    /// asiento de ingesta lo dejó pendiente y todavía no hay un asiento que lo complete.
    /// </summary>
    private static bool ControlCruzadoPendiente(IReadOnlyList<AsientoDeCustodia> cadena, Guid evidenciaId)
    {
        var propios = cadena.Where(a => a.EvidenciaId == evidenciaId).ToList();
        if (propios.Any(a => a.Tipo == TipoAsiento.ControlCruzadoCompletado))
            return false;
        var ingesta = propios.SingleOrDefault(a => a.Tipo == TipoAsiento.EvidenciaIngestada);
        if (ingesta is null)
            return false;
        using var detalle = JsonDocument.Parse(ingesta.Detalle);
        return detalle.RootElement.TryGetProperty("controlCruzado", out var control)
            && control.TryGetProperty("estado", out var estado)
            && estado.GetString() == Pendiente;
    }

    private async Task<Caso> CargarCasoAsync(Guid casoId, CancellationToken ct) =>
        await persistencia.ObtenerCasoAsync(casoId, ct)
        ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
}
