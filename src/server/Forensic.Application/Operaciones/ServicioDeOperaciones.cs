using System.Security.Cryptography;
using System.Text;
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

/// <param name="Reproducido">El hash de la salida re-ejecutada coincide byte a byte con el asentado.</param>
/// <param name="EntornoCoincide">El worker actual reporta el mismo entorno que el asentado.</param>
public sealed record ResultadoDeReproduccion(
    Guid EvidenciaId, bool Reproducido, bool EntornoCoincide,
    string Sha256Esperado, string Sha256Obtenido, string? Sha256PixelesEsperado, string Sha256PixelesObtenido,
    string EntornoAsentado, string EntornoActual);

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

    /// <summary>Formato del detalle del asiento <see cref="TipoAsiento.OperacionReproducida"/>.</summary>
    public const string FormatoDeReproduccion = "forensic-video/reproduccion/v1";

    /// <summary>
    /// El gate de la Fase 2: re-ejecuta la operación de un derivado usando sólo su asiento
    /// <see cref="TipoAsiento.OperacionAplicada"/> (tipo, parámetros exactos y entrada) y compara byte a
    /// byte el hash de la salida. Sólo confía en una cadena que verifica. Lo puede pedir cualquiera con
    /// lectura del caso, auditor incluido, y queda asentado; no se guarda la salida, que si reproduce es
    /// idéntica a la custodiada. Que no reproduzca no es un ataque (puede ser otro entorno): se informa
    /// y se asienta, con LogError. Una divergencia del worker sí es un evento de seguridad.
    /// </summary>
    public async Task<ResultadoDeReproduccion> ReproducirAsync(Guid casoId, Guid evidenciaId, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarCasoAsync(casoId, ct);
        caso.ExigirLectura(actor);
        var derivado = await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, ct)
            ?? throw new NoEncontradoException($"No existe la evidencia {evidenciaId} en el caso.");
        if (derivado.EsOriginal)
            throw new ExcepcionDeDominio("Un original no salió de ninguna operación: no hay nada que reproducir.");

        var cadena = await persistencia.ObtenerCadenaAsync(casoId, ct);
        var verificacion = RegistroDeCustodia.Verificar(casoId, cadena);
        if (!verificacion.EsIntegra)
            throw new CadenaComprometidaException(
                $"La cadena del caso no verifica (asiento {verificacion.SecuenciaRota}: {verificacion.Motivo}); no se reproduce.");
        var asiento = cadena.SingleOrDefault(a => a.Tipo == TipoAsiento.OperacionAplicada && a.EvidenciaId == derivado.Id)
            ?? throw new ExcepcionDeDominio($"La evidencia {evidenciaId} no tiene un asiento de operación.");
        var asentada = OperacionAsentada.Desde(asiento);
        if (asentada.HashSalida != derivado.Hash)
            throw new CadenaComprometidaException("El asiento de la operación no corresponde al derivado custodiado.");
        var padre = await persistencia.ObtenerEvidenciaAsync(casoId, asentada.EvidenciaEntradaId, ct)
            ?? throw new CadenaComprometidaException("La entrada asentada ya no existe en el caso.");
        // La operación tiene que seguir en el catálogo; al worker va el string asentado, sin re-serializar.
        CatalogoDeOperaciones.Interpretar(asentada.Tipo, JsonDocument.Parse(asentada.Parametros).RootElement);

        HashSha256 obtenido;
        string pixeles, entornoActual;
        await using (var crudo = await almacen.AbrirVerificadoAsync(asentada.HashEntrada, ct))
        await using (var salida = await worker.AplicarAsync(asentada.Tipo, asentada.Parametros, crudo, ct))
        {
            if (salida.HashEntrada != asentada.HashEntrada)
                throw Divergencia(casoId, padre, "entrada", asentada.HashEntrada, salida.HashEntrada);
            obtenido = HashSha256.DesdeBytes(await SHA256.HashDataAsync(salida.Contenido, ct));
            if (salida.HashSalida != obtenido)
                throw Divergencia(casoId, padre, "salida", obtenido, salida.HashSalida);
            (pixeles, entornoActual) = (salida.HashPixeles, salida.Entorno);
        }

        var resultado = new ResultadoDeReproduccion(
            derivado.Id, obtenido == asentada.HashSalida, entornoActual == asentada.Entorno,
            asentada.HashSalida.Hex, obtenido.Hex, asentada.HashPixelesSalida, pixeles, asentada.Entorno, entornoActual);
        if (!resultado.Reproducido)
            logger.LogError(
                "La operación del derivado {EvidenciaId} (caso {CasoId}) no reproduce: esperado {Esperado}, obtenido {Obtenido}. " +
                "Entorno coincide: {EntornoCoincide}.",
                derivado.Id, casoId, resultado.Sha256Esperado, resultado.Sha256Obtenido, resultado.EntornoCoincide);

        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, ct);
        persistencia.Agregar(RegistroDeCustodia.Asentar(
            ultimo, casoId, TipoAsiento.OperacionReproducida, derivado.Id,
            DetalleDeReproduccion(asiento.Secuencia, resultado), actor, reloj.GetUtcNow()));
        await persistencia.GuardarCambiosAsync(ct);
        return resultado;
    }

    private static string DetalleDeReproduccion(long secuenciaDeLaOperacion, ResultadoDeReproduccion r)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("formato", FormatoDeReproduccion);
            w.WriteNumber("asientoDeLaOperacion", secuenciaDeLaOperacion);
            w.WriteBoolean("reproducido", r.Reproducido);
            w.WriteBoolean("entornoCoincide", r.EntornoCoincide);
            w.WriteString("sha256Esperado", r.Sha256Esperado);
            w.WriteString("sha256Obtenido", r.Sha256Obtenido);
            w.WriteString("sha256PixelesEsperado", r.Sha256PixelesEsperado);
            w.WriteString("sha256PixelesObtenido", r.Sha256PixelesObtenido);
            w.WritePropertyName("entorno");
            w.WriteRawValue(r.EntornoActual);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
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
