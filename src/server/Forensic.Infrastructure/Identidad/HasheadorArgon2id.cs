using System.Security.Cryptography;
using Forensic.Application.Identidad;
using Forensic.Domain.Identidad;
using Sodium;

namespace Forensic.Infrastructure.Identidad;

/// <summary>
/// Argon2id de libsodium (<c>crypto_pwhash_str</c>): genera la sal, codifica en PHC y verifica en
/// tiempo constante. Parámetros «interactive» de libsodium (t=2, m=64 MiB), por encima del mínimo de
/// OWASP. No hay criptografía propia: este adaptador sólo traduce tipos.
/// </summary>
internal sealed class HasheadorArgon2id : IHasheadorDeContrasena
{
    private static readonly Lazy<HashDeContrasena> Ficticio = new(() =>
        Calcular(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public HashDeContrasena HashFicticio => Ficticio.Value;

    public HashDeContrasena Hashear(ContrasenaAceptada contrasena) => Calcular(contrasena.Valor);

    public bool Verificar(string contrasena, HashDeContrasena hash) =>
        PasswordHash.ArgonHashStringVerify(hash.Phc, contrasena ?? "");

    private static HashDeContrasena Calcular(string contrasena) =>
        HashDeContrasena.Parse(PasswordHash.ArgonHashString(contrasena, PasswordHash.StrengthArgon.Interactive).TrimEnd('\0'));
}
