using Forensic.Application.Custodia;
using Forensic.Domain.Casos;
using Forensic.Domain.Custodia;

namespace Forensic.Application.Casos;

public sealed record CasoDto(
    Guid Id, string NumeroExpediente, string Descripcion, EstadoCaso Estado,
    DateTimeOffset AbiertoEn, string AbiertoPor, IReadOnlyList<string> AnalistasAsignados)
{
    internal static CasoDto Desde(Caso c) =>
        new(c.Id, c.NumeroExpediente, c.Descripcion, c.Estado, c.AbiertoEn, c.AbiertoPor, c.AnalistasAsignados.ToList());
}

public sealed class ServicioDeCasos(IPersistenciaForense persistencia, IActorActual actorActual, TimeProvider reloj)
{
    public async Task<CasoDto> AbrirAsync(string numeroExpediente, string descripcion, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var ahora = reloj.GetUtcNow();
        var caso = Caso.Abrir(numeroExpediente, descripcion, actor, ahora);

        var asiento = RegistroDeCustodia.Asentar(
            ultimo: null, caso.Id, TipoAsiento.CasoAbierto, evidenciaId: null,
            DetalleDeAsiento.Serializar(new { caso.NumeroExpediente, caso.Descripcion, caso.AnalistasAsignados }),
            actor, ahora);

        persistencia.Agregar(caso);
        persistencia.Agregar(asiento);
        await persistencia.GuardarCambiosAsync(ct);
        return CasoDto.Desde(caso);
    }

    public async Task<CasoDto> ObtenerAsync(Guid casoId, CancellationToken ct)
    {
        var caso = await CargarAsync(casoId, ct);
        caso.ExigirLectura(actorActual.Actor);
        return CasoDto.Desde(caso);
    }

    public async Task<IReadOnlyList<CasoDto>> ListarAsync(CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var casos = await persistencia.ListarCasosAsync(ct);
        return casos.Where(c => c.PuedeLeer(actor)).Select(CasoDto.Desde).ToList();
    }

    public async Task<CasoDto> AsignarAnalistaAsync(Guid casoId, string usuario, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var caso = await CargarAsync(casoId, ct);
        caso.AsignarAnalista(usuario, actor);

        await AsentarAsync(caso, TipoAsiento.AnalistaAsignado, DetalleDeAsiento.Serializar(new { usuario }), ct);
        return CasoDto.Desde(caso);
    }

    /// <summary>
    /// Los controles cruzados pendientes no bloquean el cierre (lo acoplarían a que el worker esté
    /// disponible): el asiento registra cuántos quedaban, y un supervisor puede completarlos después.
    /// </summary>
    public async Task<CasoDto> CerrarAsync(Guid casoId, CancellationToken ct)
    {
        var caso = await CargarAsync(casoId, ct);
        caso.Cerrar(actorActual.Actor);

        var crucesPendientes = ControlesCruzados.Pendientes(await persistencia.ObtenerCadenaAsync(casoId, ct)).Count;
        await AsentarAsync(caso, TipoAsiento.CasoCerrado, DetalleDeAsiento.Serializar(new { crucesPendientes }), ct);
        return CasoDto.Desde(caso);
    }

    private async Task AsentarAsync(Caso caso, TipoAsiento tipo, string detalle, CancellationToken ct)
    {
        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(caso.Id, ct);
        persistencia.Agregar(RegistroDeCustodia.Asentar(
            ultimo, caso.Id, tipo, evidenciaId: null, detalle, actorActual.Actor, reloj.GetUtcNow()));
        await persistencia.GuardarCambiosAsync(ct);
    }

    private async Task<Caso> CargarAsync(Guid casoId, CancellationToken ct) =>
        await persistencia.ObtenerCasoAsync(casoId, ct)
        ?? throw new NoEncontradoException($"No existe el caso {casoId}.");
}
