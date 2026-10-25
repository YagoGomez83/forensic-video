using System.Text.RegularExpressions;
using Forensic.Domain.Comun;

namespace Forensic.Domain.Identidad;

/// <summary>Por qué no autenticó un usuario. Sólo va a la auditoría de accesos, nunca al cliente.</summary>
public enum MotivoDeRechazo
{
    UsuarioInexistente = 1,
    ContrasenaIncorrecta = 2,
    Bloqueado = 3,
    Deshabilitado = 4,
}

/// <summary>
/// Usuario del sistema. Lo administra un admin; nadie se administra a sí mismo, así que siempre queda
/// al menos un admin activo (el que actúa). No se borra: se deshabilita.
/// </summary>
public sealed partial class Usuario
{
    public const int MaximoDeIntentosFallidos = 5;
    public static readonly TimeSpan DuracionDelBloqueo = TimeSpan.FromMinutes(15);

    /// <summary>Autor del admin inicial, sembrado desde la configuración al arrancar.</summary>
    public const string Sistema = "sistema";

    private Usuario()
    {
        Nombre = null!;
        HashDeContrasena = null!;
        CreadoPor = null!;
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; }
    public Rol Rol { get; private set; }
    public HashDeContrasena HashDeContrasena { get; private set; }
    public bool Activo { get; private set; }
    public bool DebeCambiarContrasena { get; private set; }
    public int IntentosFallidos { get; private set; }
    public DateTimeOffset? BloqueadoHasta { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public string CreadoPor { get; private set; }

    /// <summary>Alta por un admin. El usuario debe cambiar la contraseña inicial en su primer acceso.</summary>
    public static Usuario Crear(string nombre, Rol rol, HashDeContrasena hash, Actor admin, DateTimeOffset ahora)
    {
        ExigirAdmin(admin);
        return Nuevo(nombre, rol, hash, admin.Usuario, ahora);
    }

    /// <summary>Primer admin, sembrado al arrancar cuando todavía no existe ninguno.</summary>
    public static Usuario CrearAdminInicial(string nombre, HashDeContrasena hash, DateTimeOffset ahora) =>
        Nuevo(nombre, Rol.Admin, hash, Sistema, ahora);

    public Actor ComoActor() => new(Nombre, Rol);

    /// <summary>
    /// Registra un intento de login cuya contraseña ya verificó el hasheador. Devuelve null si
    /// autentica. Un usuario deshabilitado o bloqueado no autentica ni con la contraseña correcta.
    /// </summary>
    public MotivoDeRechazo? Autenticar(bool contrasenaValida, DateTimeOffset ahora)
    {
        if (!Activo)
            return MotivoDeRechazo.Deshabilitado;
        if (BloqueadoHasta is { } hasta && ahora < hasta)
            return MotivoDeRechazo.Bloqueado;

        if (!contrasenaValida)
        {
            IntentosFallidos++;
            if (IntentosFallidos >= MaximoDeIntentosFallidos)
            {
                BloqueadoHasta = Tiempo.NormalizarParaCustodia(ahora + DuracionDelBloqueo);
                IntentosFallidos = 0;
            }
            return MotivoDeRechazo.ContrasenaIncorrecta;
        }

        IntentosFallidos = 0;
        BloqueadoHasta = null;
        return null;
    }

    public void CambiarRol(Rol nuevo, Actor admin)
    {
        ExigirAdminSobreOtro(admin);
        ExigirRolValido(nuevo);
        Rol = nuevo;
    }

    public void Deshabilitar(Actor admin)
    {
        ExigirAdminSobreOtro(admin);
        Activo = false;
    }

    /// <summary>Un admin fija una contraseña temporal: el usuario debe cambiarla al entrar.</summary>
    public void ResetearContrasena(HashDeContrasena nuevo, Actor admin)
    {
        ExigirAdminSobreOtro(admin);
        HashDeContrasena = nuevo ?? throw new ExcepcionDeDominio("Falta el hash de la contraseña.");
        DebeCambiarContrasena = true;
        IntentosFallidos = 0;
        BloqueadoHasta = null;
    }

    /// <summary>Cada uno cambia la suya; la contraseña actual la verifica antes la aplicación.</summary>
    public void CambiarContrasena(HashDeContrasena nuevo, Actor actor)
    {
        if (actor.Usuario != Nombre)
            throw new AccesoDenegadoException("Cada usuario cambia sólo su propia contraseña.");
        HashDeContrasena = nuevo ?? throw new ExcepcionDeDominio("Falta el hash de la contraseña.");
        DebeCambiarContrasena = false;
    }

    private static Usuario Nuevo(string nombre, Rol rol, HashDeContrasena hash, string creadoPor, DateTimeOffset ahora)
    {
        if (nombre is null || !NombreValido().IsMatch(nombre))
            throw new ExcepcionDeDominio("El usuario debe tener de 1 a 100 caracteres: letras, números, '.', '_' o '-'.");
        ExigirRolValido(rol);
        return new Usuario
        {
            Id = Guid.NewGuid(),
            Nombre = nombre,
            Rol = rol,
            HashDeContrasena = hash ?? throw new ExcepcionDeDominio("Falta el hash de la contraseña."),
            Activo = true,
            DebeCambiarContrasena = true,
            CreadoEn = Tiempo.NormalizarParaCustodia(ahora),
            CreadoPor = creadoPor,
        };
    }

    private static void ExigirRolValido(Rol rol)
    {
        if (!Enum.IsDefined(rol))
            throw new ExcepcionDeDominio("El rol es obligatorio: Analista, Supervisor, Auditor o Admin.");
    }

    private static void ExigirAdmin(Actor actor)
    {
        if (actor.Rol != Rol.Admin)
            throw new AccesoDenegadoException("Sólo un admin administra usuarios.");
    }

    private void ExigirAdminSobreOtro(Actor admin)
    {
        ExigirAdmin(admin);
        if (admin.Usuario == Nombre)
            throw new ExcepcionDeDominio("Un admin no puede administrarse a sí mismo.");
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex NombreValido();
}
