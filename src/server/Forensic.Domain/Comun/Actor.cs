namespace Forensic.Domain.Comun;

public enum Rol
{
    Analista = 1,
    Supervisor = 2,
    Auditor = 3,
    /// <summary>Administra usuarios y roles. No accede a casos (separación de funciones).</summary>
    Admin = 4,
}

/// <summary>Usuario autenticado que ejecuta una acción. Lo determina siempre el servidor.</summary>
public sealed record Actor
{
    public Actor(string usuario, Rol rol)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ExcepcionDeDominio("El actor debe tener usuario.");
        if (!Enum.IsDefined(rol))
            throw new ExcepcionDeDominio("Rol inválido.");
        Usuario = usuario;
        Rol = rol;
    }

    public string Usuario { get; }
    public Rol Rol { get; }
}
