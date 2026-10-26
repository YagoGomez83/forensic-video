using System.Text.Json;
using Forensic.Application.Evidencias;
using Forensic.Application.Operaciones;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;
using Forensic.Domain.Operaciones;
using Microsoft.Extensions.Logging;

namespace Forensic.Application.Ia;

public sealed class ServicioDeOperacionesIa(
    IPersistenciaForense persistencia,
    IAlmacenDeEvidencia almacen,
    IActorActual actorActual,
    TimeProvider reloj,
    ILogger<ServicioDeOperacionesIa> logger)
{
    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Registra una operación de IA hecha en la workstation (super-resolución): el derivado PNG y el
    /// reporte del worker, tal cual. El servidor no la puede re-ejecutar: valida la procedencia (catálogo,
    /// lista blanca, la entrada es la evidencia custodiada), hashea él mismo el PNG y lo cruza con el
    /// reportado, y custodia el derivado «sólo investigación». Operación, derivado y asiento
    /// <see cref="TipoAsiento.OperacionIaAplicada"/> van en una sola transacción.
    /// </summary>
    /// <param name="salida">El PNG del worker; tiene que poder releerse desde el principio.</param>
    public async Task<OperacionAplicadaDto> RegistrarAsync(
        Guid casoId, Guid evidenciaId, string reporte, Stream salida, string? nombre, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await persistencia.ObtenerCasoAsync(casoId, ct)
            ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
        caso.ExigirOperacion(actor);
        var entrada = await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, ct)
            ?? throw new NoEncontradoException($"No existe la evidencia {evidenciaId} en el caso.");

        using var documento = Parsear(reporte);
        var r = documento.RootElement;
        var operacion = CatalogoDeIa.InterpretarOperacion(Texto(r, "operacion"), Propiedad(r, "parametros"));
        var m = Propiedad(r, "modelo");
        var modelo = new ModeloDeIa(Texto(m, "id"), Texto(m, "version"), HashSha256.Parse(Texto(m, "sha256")));
        var hashEntrada = HashSha256.Parse(Texto(Propiedad(r, "entrada"), "sha256"));
        var hashSalidaReportado = HashSha256.Parse(Texto(Propiedad(r, "salida"), "sha256"));
        var entorno = Propiedad(r, "entorno");
        if (entorno.ValueKind != JsonValueKind.Object)
            throw new ExcepcionDeDominio("El reporte requiere 'entorno' como objeto JSON.");
        if (hashEntrada != entrada.Hash)
        {
            logger.LogWarning(
                "Operación de IA sobre otro contenido: caso {CasoId}, evidencia {EvidenciaId} ({Hash}), declarado {Declarado}, actor {Actor}.",
                casoId, evidenciaId, entrada.Hash.Hex, hashEntrada.Hex, actor.Usuario);
            throw new ExcepcionDeDominio(
                $"El worker procesó otro contenido ({hashEntrada}), no la evidencia {evidenciaId} ({entrada.Hash}).");
        }
        await ExigirPngAsync(salida, ct);

        // El servidor hashea el PNG que recibió: fuente de verdad. Si no es el que reportó el worker, se
        // corrompió en el camino o alguien lo cambió. (El blob queda huérfano en el almacén direccionado
        // por hash, como en las operaciones deterministas: sin asiento no existe para la custodia.)
        var almacenado = await almacen.GuardarAsync(salida, ct);
        if (almacenado.Hash != hashSalidaReportado)
        {
            logger.LogWarning(
                "El PNG recibido no es el que reportó el worker de IA: caso {CasoId}, evidencia {EvidenciaId}, recibido {Recibido}, reportado {Reportado}.",
                casoId, evidenciaId, almacenado.Hash.Hex, hashSalidaReportado.Hex);
            throw new ExcepcionDeDominio(
                $"El derivado recibido ({almacenado.Hash}) no es el que reportó el worker ({hashSalidaReportado}).");
        }

        var ahora = reloj.GetUtcNow();
        var registrada = OperacionForense.RegistrarIa(
            caso, entrada, operacion, modelo, entorno.GetRawText(), almacenado.Hash, almacenado.TamanoBytes, actor, ahora);
        var derivado = entrada.Derivar(registrada, nombre ?? NombrePorDefecto(entrada, operacion));
        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, ct);
        var asiento = RegistroDeCustodia.Asentar(
            ultimo, casoId, TipoAsiento.OperacionIaAplicada, derivado.Id, registrada.DetalleDeAsiento(derivado), actor, ahora);

        persistencia.Agregar(registrada);
        persistencia.Agregar(derivado);
        persistencia.Agregar(asiento);
        await persistencia.GuardarCambiosAsync(ct);
        return new OperacionAplicadaDto(
            EvidenciaDto.Desde(derivado, controlCruzadoPendiente: false), registrada.Id, registrada.Tipo, registrada.Parametros,
            registrada.Entorno);
    }

    private static async Task ExigirPngAsync(Stream salida, CancellationToken ct)
    {
        if (!salida.CanSeek)
            throw new InvalidOperationException("El derivado tiene que poder releerse.");
        var cabecera = new byte[FirmaPng.Length];
        var leidos = await salida.ReadAtLeastAsync(cabecera, cabecera.Length, throwOnEndOfStream: false, ct);
        salida.Position = 0;
        if (leidos < FirmaPng.Length || !cabecera.AsSpan().SequenceEqual(FirmaPng))
            throw new ExcepcionDeDominio("El derivado de una operación de IA tiene que ser PNG.");
    }

    /// <summary><c>cam1.png</c> + <c>ia.superresolucion/v1</c> → <c>cam1-superresolucion.png</c>.</summary>
    private static string NombrePorDefecto(Evidencia entrada, DefinicionDeOperacion operacion)
    {
        var baseNombre = Path.GetFileNameWithoutExtension(entrada.NombreOriginal);
        var nombre = $"{baseNombre}-{operacion.Tipo.Split('/')[0].Split('.')[^1]}.{OperacionForense.FormatoDeSalida}";
        return nombre.Length <= Evidencia.LongitudMaximaNombre ? nombre : nombre[^Evidencia.LongitudMaximaNombre..];
    }

    private static JsonDocument Parsear(string reporte)
    {
        try
        {
            return JsonDocument.Parse(reporte);
        }
        catch (JsonException ex)
        {
            throw new ExcepcionDeDominio($"El reporte del worker no es JSON: {ex.Message}");
        }
    }

    private static JsonElement Propiedad(JsonElement objeto, string nombre) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(nombre, out var valor)
            ? valor
            : throw new ExcepcionDeDominio($"Al reporte le falta '{nombre}'.");

    private static string Texto(JsonElement objeto, string nombre) =>
        Propiedad(objeto, nombre) is { ValueKind: JsonValueKind.String } valor
            ? valor.GetString()!
            : throw new ExcepcionDeDominio($"En el reporte, '{nombre}' tiene que ser un texto.");
}
