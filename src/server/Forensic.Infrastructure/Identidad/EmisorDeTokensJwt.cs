using System.Security.Claims;
using System.Text;
using Forensic.Application;
using Forensic.Application.Identidad;
using Forensic.Domain.Comun;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Forensic.Infrastructure.Identidad;

public sealed class OpcionesJwt
{
    public const string Seccion = "Identidad:Jwt";
    public const int LargoMinimoDeClave = 32;

    /// <summary>Clave HMAC de al menos 32 bytes. Sólo desde secretos: nunca en appsettings ni en la imagen.</summary>
    public string? ClaveDeFirma { get; set; }

    public string Emisor { get; set; } = "forensic-video";
    public string Audiencia { get; set; } = "forensic-video";
    public TimeSpan Duracion { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Tolerancia al desfase de relojes al validar la expiración.</summary>
    public TimeSpan Tolerancia { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Lanza si falta la clave o es corta: el servidor no arranca sin ella.</summary>
    public SymmetricSecurityKey Clave()
    {
        var bytes = Encoding.UTF8.GetBytes(ClaveDeFirma ?? "");
        if (bytes.Length < LargoMinimoDeClave)
            throw new InvalidOperationException(
                $"Falta {Seccion}:ClaveDeFirma o tiene menos de {LargoMinimoDeClave} bytes " +
                "(user-secrets o variable Identidad__Jwt__ClaveDeFirma).");
        return new SymmetricSecurityKey(bytes);
    }

    public TokenValidationParameters ParametrosDeValidacion() => new()
    {
        ValidIssuer = Emisor,
        ValidAudience = Audiencia,
        IssuerSigningKey = Clave(),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = Tolerancia,
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = ClaimTypes.Role,
    };
}

/// <summary>JWT HS256 de vida corta. Sin refresh tokens: al vencer, se vuelve a iniciar sesión.</summary>
internal sealed class EmisorDeTokensJwt(IOptions<OpcionesJwt> opciones, TimeProvider reloj) : IEmisorDeTokens
{
    private readonly JsonWebTokenHandler _handler = new();

    public TokenEmitido Emitir(Actor actor)
    {
        var o = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var expira = ahora + o.Duracion;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Emisor,
            Audience = o.Audiencia,
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = expira.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = actor.Usuario,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                [ClaimTypes.Role] = actor.Rol.ToString(),
            },
            SigningCredentials = new SigningCredentials(o.Clave(), SecurityAlgorithms.HmacSha256),
        });
        return new TokenEmitido(token, expira);
    }
}
