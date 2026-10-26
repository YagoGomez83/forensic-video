using Forensic.Domain.Comun;

namespace Forensic.Domain.Ia;

/// <summary>
/// El modelo que produjo un resultado de IA: id, versión y el SHA-256 de sus pesos. Es procedencia,
/// no garantía de reproducción: la misma entrada en otra GPU puede dar otra salida.
/// </summary>
public sealed record ModeloDeIa(string Id, string Version, HashSha256 Sha256);
