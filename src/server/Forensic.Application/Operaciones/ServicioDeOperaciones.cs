using System.Text.Json;
using Forensic.Application.Evidencias;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Operaciones;
using Microsoft.Extensions.Logging;

namespace Forensic.Application.Operaciones;

public sealed record OperacionAplicadaDto(EvidenciaDto Derivado, Guid OperacionId, string Tipo, string Parametros, string Entorno);

public sealed class ServicioDeOperaciones(
    IPersistenciaForense persistencia,
    IAlmacenDeEvidencia almacen,
    IWorkerDeProcesamiento worker,
    IActorActual actorActual,
    TimeProvider reloj,
    ILogger<ServicioDeOperaciones> logger)
{
    /// <summary>
    /// Aplica una operación del catálogo y produce un derivado nuevo; el original no se toca. Orden:
    /// autorizar y validar antes de mandar nada al worker; mandarle la entrada releída con verificación
    /// de hash; guardar la salida (el almacén la hashea: fuente de verdad); cruzar los hashes que reporta
    /// el worker. Operación, derivado y asiento se guardan en una sola transacción: si el asiento falla,
    /// la operación no ocurrió.
    /// </summary>
    public async Task<OperacionAplicadaDto> AplicarAsync(
        Guid casoId, Guid evidenciaId, string tipo, JsonElement parametros, string? nombre, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        caso.ExigirOperacion(actor);
        var definicion = CatalogoDeOperaciones.Interpretar(tipo, parametros);
        var entrada = await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, ct)
            ?? throw new NoEncontradoException($"No existe la evidencia {evidenciaId} en el caso.");

        ContenidoAlmacenado almacenado;
        string entorno, hashPixeles;
        await using (var crudo = await almacen.AbrirVerificadoAsync(entrada.Hash, ct))
        await using (var salida = await worker.AplicarAsync(definicion.Tipo, definicion.ParametrosCanonicos, crudo, ct))
        {
            if (salida.HashEntrada != entrada.Hash)
                throw Divergencia(casoId, entrada, "entrada", entrada.Hash, salida.HashEntrada);
            almacenado = await almacen.GuardarAsync(salida.Contenido, ct);
            if (salida.HashSalida != almacenado.Hash)
                throw Divergencia(casoId, entrada, "salida", almacenado.Hash, salida.HashSalida);
            (entorno, hashPixeles) = (salida.Entorno, salida.HashPixeles);
        }

        // El reloj después del procesamiento, que puede tardar (como en la ingesta).
        var ahora = reloj.GetUtcNow();
        var operacion = OperacionForense.Registrar(
            caso, entrada, definicion, entorno, almacenado.Hash, almacenado.TamanoBytes, hashPixeles, actor, ahora);
        var derivado = entrada.Derivar(operacion, nombre ?? NombrePorDefecto(entrada, definicion));
        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, ct);
        var asiento = RegistroDeCustodia.Asentar(
            ultimo, casoId, TipoAsiento.OperacionAplicada, derivado.Id, operacion.DetalleDeAsiento(derivado), actor, ahora);

        persistencia.Agregar(operacion);
        persistencia.Agregar(derivado);
        persistencia.Agregar(asiento);
        await persistencia.GuardarCambiosAsync(ct);
        return new OperacionAplicadaDto(
            EvidenciaDto.Desde(derivado, controlCruzadoPendiente: false), operacion.Id, operacion.Tipo, operacion.Parametros, operacion.Entorno);
    }

    /// <summary><c>cam1.png</c> + <c>opencv.recorte/v1</c> → <c>cam1-recorte.png</c>. Es metadato, nunca una ruta.</summary>
    private static string NombrePorDefecto(Evidencia entrada, DefinicionDeOperacion definicion)
    {
        var baseNombre = Path.GetFileNameWithoutExtension(entrada.NombreOriginal);
        var operacion = definicion.Tipo.Split('/')[0].Split('.')[^1];
        var nombre = $"{baseNombre}-{operacion}.{OperacionForense.FormatoDeSalida}";
        return nombre.Length <= Evidencia.LongitudMaximaNombre ? nombre : nombre[^Evidencia.LongitudMaximaNombre..];
    }

    /// <summary>
    /// La entrada salió verificada del almacén y la salida la hashea el servidor: si el worker reporta
    /// otra cosa, hubo corrupción en tránsito o el worker está roto o comprometido.
    /// </summary>
    private DivergenciaDeControlCruzadoException Divergencia(
        Guid casoId, Evidencia entrada, string lado, HashSha256 servidor, HashSha256 worker)
    {
        logger.LogCritical(
            "Divergencia en el control cruzado del worker de procesamiento ({Lado}): caso {CasoId}, evidencia {EvidenciaId}. " +
            "Servidor {Hash}, worker {HashWorker}.",
            lado, casoId, entrada.Id, servidor.Hex, worker.Hex);
        return new DivergenciaDeControlCruzadoException(
            $"El worker de procesamiento reportó {worker} para la {lado}; el servidor tiene {servidor}.");
    }

    private async Task<Caso> CargarCasoAsync(Guid casoId, CancellationToken ct) =>
        await persistencia.ObtenerCasoAsync(casoId, ct)
        ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
}
