using Forensic.Domain.Comun;

namespace Forensic.Domain.Identidad;

/// <summary>
/// Una contraseña que ya pasó la <see cref="PoliticaDeContrasena"/>. Es lo único que acepta el
/// hasheador al crear un hash, así que no se puede guardar una contraseña sin validarla.
/// </summary>
public sealed class ContrasenaAceptada
{
    internal ContrasenaAceptada(string valor) => Valor = valor;

    public string Valor { get; }

    /// <summary>No se expone en logs ni mensajes de error.</summary>
    public override string ToString() => "(contraseña)";
}

/// <summary>
/// Al estilo NIST SP 800-63B: largo mínimo y sin reglas de composición. El máximo acota el costo de
/// Argon2 ante entradas enormes.
/// </summary>
public static class PoliticaDeContrasena
{
    public const int LargoMinimo = 12;
    public const int LargoMaximo = 128;

    public static ContrasenaAceptada Validar(string contrasena, string usuario)
    {
        if (string.IsNullOrWhiteSpace(contrasena) || contrasena.Length < LargoMinimo)
            throw new ExcepcionDeDominio($"La contraseña debe tener al menos {LargoMinimo} caracteres.");
        if (contrasena.Length > LargoMaximo)
            throw new ExcepcionDeDominio($"La contraseña admite hasta {LargoMaximo} caracteres.");
        if (!string.IsNullOrEmpty(usuario) && contrasena.Contains(usuario, StringComparison.OrdinalIgnoreCase))
            throw new ExcepcionDeDominio("La contraseña no puede contener el nombre de usuario.");
        return new ContrasenaAceptada(contrasena);
    }
}
