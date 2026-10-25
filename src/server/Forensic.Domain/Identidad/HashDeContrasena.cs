using Forensic.Domain.Comun;

namespace Forensic.Domain.Identidad;

/// <summary>
/// Hash Argon2id de una contraseña en formato PHC (<c>$argon2id$v=19$m=…,t=…,p=…$sal$hash</c>): lleva
/// sus parámetros y su sal. Es opaco para el dominio; sólo lo produce y verifica el hasheador. Nunca
/// se guarda ni se loguea una contraseña en claro.
/// </summary>
public sealed record HashDeContrasena
{
    private const string Prefijo = "$argon2id$";

    private HashDeContrasena(string phc) => Phc = phc;

    public string Phc { get; }

    public static HashDeContrasena Parse(string phc)
    {
        if (phc is null || !phc.StartsWith(Prefijo, StringComparison.Ordinal) || phc.Length > 512)
            throw new ExcepcionDeDominio("Un hash de contraseña debe estar en formato PHC de Argon2id.");
        return new HashDeContrasena(phc);
    }

    /// <summary>No se expone en logs ni mensajes de error.</summary>
    public override string ToString() => "(hash de contraseña)";
}
