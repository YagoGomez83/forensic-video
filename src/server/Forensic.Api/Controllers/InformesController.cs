using Forensic.Application.Informes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Forensic.Api.Controllers;

public sealed class OpcionesDeInformes
{
    public const string Seccion = "Informes";

    /// <summary>
    /// Por encima de este tamaño una evidencia no se embebe en el bundle: va sólo su hash y el archivo
    /// viaja aparte. Por defecto 512 MiB, para que un export de DVR de varios GB no infle el bundle.
    /// </summary>
    public long UmbralDeArchivoEmbebidoBytes { get; set; } = 512L * 1024 * 1024;
}

/// <summary>
/// Informes de un caso. El pericial y el investigativo se emiten y se descargan por rutas distintas;
/// ninguna ruta los combina.
/// </summary>
[ApiController]
[Route("casos/{casoId:guid}/informes")]
public sealed class InformesController(
    ServicioDeInformePericial pericial, ServicioDeInformeInvestigativo investigativo, ConsultaDeInformes consulta,
    IOptions<OpcionesDeInformes> opciones) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<InformeEmitidoDto>> Listar(Guid casoId, CancellationToken ct) => consulta.ListarAsync(casoId, ct);

    /// <summary>Emite el informe pericial (sólo lo admisible en juicio) y asienta su emisión.</summary>
    [HttpPost("pericial")]
    public async Task<ActionResult<InformeEmitidoDto>> EmitirPericial(Guid casoId, CancellationToken ct)
    {
        var informe = await pericial.EmitirAsync(casoId, opciones.Value.UmbralDeArchivoEmbebidoBytes, ct);
        return Created($"/casos/{casoId}/informes/pericial/{informe.Secuencia}", informe);
    }

    [HttpGet("pericial/{secuencia:long}/pdf")]
    public Task<IActionResult> PdfPericial(Guid casoId, long secuencia, CancellationToken ct) =>
        ArchivoAsync(casoId, ServicioDeInformePericial.Tipo, secuencia, false, ct);

    [HttpGet("pericial/{secuencia:long}/bundle")]
    public Task<IActionResult> BundlePericial(Guid casoId, long secuencia, CancellationToken ct) =>
        ArchivoAsync(casoId, ServicioDeInformePericial.Tipo, secuencia, true, ct);

    /// <summary>Emite el informe investigativo (con la IA, «sólo investigación») y asienta su emisión.</summary>
    [HttpPost("investigativo")]
    public async Task<ActionResult<InformeEmitidoDto>> EmitirInvestigativo(Guid casoId, CancellationToken ct)
    {
        var informe = await investigativo.EmitirAsync(casoId, opciones.Value.UmbralDeArchivoEmbebidoBytes, ct);
        return Created($"/casos/{casoId}/informes/investigativo/{informe.Secuencia}", informe);
    }

    [HttpGet("investigativo/{secuencia:long}/pdf")]
    public Task<IActionResult> PdfInvestigativo(Guid casoId, long secuencia, CancellationToken ct) =>
        ArchivoAsync(casoId, ServicioDeInformeInvestigativo.Tipo, secuencia, false, ct);

    [HttpGet("investigativo/{secuencia:long}/bundle")]
    public Task<IActionResult> BundleInvestigativo(Guid casoId, long secuencia, CancellationToken ct) =>
        ArchivoAsync(casoId, ServicioDeInformeInvestigativo.Tipo, secuencia, true, ct);

    private async Task<IActionResult> ArchivoAsync(Guid casoId, string tipo, long secuencia, bool bundle, CancellationToken ct)
    {
        var archivo = await consulta.AbrirAsync(casoId, tipo, secuencia, bundle, ct);
        return File(archivo.Contenido, archivo.TipoDeContenido, archivo.NombreDeArchivo);
    }
}
