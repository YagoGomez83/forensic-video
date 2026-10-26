using System.Text.Json;

namespace Forensic.Api.Controllers;

/// <param name="Tipo">Tipo versionado del catálogo, p. ej. <c>opencv.recorte/v1</c>.</param>
/// <param name="Parametros">Objeto JSON; el servidor lo canonicaliza antes de asentarlo.</param>
/// <param name="Nombre">Nombre del derivado; por defecto, el del padre con el nombre de la operación.</param>
public sealed record AplicarOperacionRequest(string Tipo, JsonElement Parametros, string? Nombre = null);
