using Forensic.Application.Casos;
using Forensic.Application.Custodia;
using Microsoft.AspNetCore.Mvc;

namespace Forensic.Api.Controllers;

public sealed record AbrirCasoRequest(string NumeroExpediente, string Descripcion);

public sealed record AsignarAnalistaRequest(string Usuario);

[ApiController]
[Route("casos")]
public sealed class CasosController(ServicioDeCasos casos, ServicioDeCustodia custodia) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CasoDto>> Abrir(AbrirCasoRequest req, CancellationToken ct)
    {
        var caso = await casos.AbrirAsync(req.NumeroExpediente, req.Descripcion, ct);
        return CreatedAtAction(nameof(Obtener), new { casoId = caso.Id }, caso);
    }

    [HttpGet]
    public Task<IReadOnlyList<CasoDto>> Listar(CancellationToken ct) => casos.ListarAsync(ct);

    [HttpGet("{casoId:guid}")]
    public Task<CasoDto> Obtener(Guid casoId, CancellationToken ct) => casos.ObtenerAsync(casoId, ct);

    [HttpPost("{casoId:guid}/analistas")]
    public Task<CasoDto> AsignarAnalista(Guid casoId, AsignarAnalistaRequest req, CancellationToken ct) =>
        casos.AsignarAnalistaAsync(casoId, req.Usuario, ct);

    [HttpPost("{casoId:guid}/cerrar")]
    public Task<CasoDto> Cerrar(Guid casoId, CancellationToken ct) => casos.CerrarAsync(casoId, ct);

    /// <summary>Export del registro de custodia del caso, con la verificación de la cadena.</summary>
    [HttpGet("{casoId:guid}/custodia")]
    public Task<ExportDeCustodia> Custodia(Guid casoId, CancellationToken ct) => custodia.ExportarAsync(casoId, ct);
}
