using System.Security.Cryptography;

namespace ForensicVideo.Domain.Comun;

/// <summary>
/// Hash SHA-256 en hexadecimal minúscula. Es la identidad criptográfica de una evidencia
/// y el eslabón de la cadena de custodia.
/// </summary>
public readonly record struct HashSha256
{
    public const int LongitudHex = 64;

    private readonly string? _hex;

    private HashSha256(string hex) => _hex = hex;

    /// <summary>Valor del hash. Un <c>default(HashSha256)</c> es inválido y no puede usarse.</summary>
    public string Hex => _hex ?? throw new ExcepcionDeDominio("Hash SHA-256 no inicializado.");

    public bool EstaInicializado => _hex is not null;

    /// <summary>Hash "anterior" del primer asiento de una cadena.</summary>
    public static HashSha256 Genesis { get; } = new(new string('0', LongitudHex));

    public static HashSha256 Parse(string hex)
    {
        if (hex is null || hex.Length != LongitudHex || !hex.All(char.IsAsciiHexDigitLower))
            throw new ExcepcionDeDominio("Un hash SHA-256 debe ser de 64 caracteres hexadecimales en minúscula.");
        return new HashSha256(hex);
    }

    public static HashSha256 DesdeBytes(ReadOnlySpan<byte> hash)
    {
        if (hash.Length != SHA256.HashSizeInBytes)
            throw new ExcepcionDeDominio("Un hash SHA-256 debe tener 32 bytes.");
        return new HashSha256(Convert.ToHexString(hash).ToLowerInvariant());
    }

    public static HashSha256 Calcular(ReadOnlySpan<byte> datos) => DesdeBytes(SHA256.HashData(datos));

    public override string ToString() => _hex ?? "(sin hash)";
}
