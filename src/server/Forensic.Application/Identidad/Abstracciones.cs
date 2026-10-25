using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;

namespace Forensic.Application.Identidad;

/// <summary>Usuarios del sistema. Comparte la unidad de trabajo con <see cref="IAuditoriaDeAccesos"/>.</summary>
public interface IRepositorioDeUsuarios
{
    Task<Usuario?> ObtenerAsync(string nombre, CancellationToken ct);
    Task<IReadOnlyList<Usuario>> ListarAsync(CancellationToken ct);
    Task<bool> ExisteAdminActivoAsync(CancellationToken ct);
    void Agregar(Usuario usuario);

    /// <summary>
    /// Confirma en una sola transacción los cambios de usuarios y los eventos de auditoría
    /// registrados. Si otro proceso modificó el mismo usuario primero, lanza
    /// <see cref="ConflictoDeIdentidadException"/> y no se guarda nada.
    /// </summary>
    Task GuardarCambiosAsync(CancellationToken ct);
}

public enum TipoDeEventoDeAcceso
{
    LoginExitoso = 1,
    LoginFallido = 2,
    CuentaBloqueada = 3,
    UsuarioCreado = 4,
    RolCambiado = 5,
    ContrasenaCambiada = 6,
    ContrasenaReseteada = 7,
    UsuarioDeshabilitado = 8,
    AdminInicialSembrado = 9,
}

/// <param name="Usuario">El usuario afectado (en un login, el nombre tal como se intentó).</param>
/// <param name="Actor">Quién hizo la acción; en un login, el propio usuario.</param>
/// <param name="Detalle">Motivo de un rechazo, rol nuevo, etc. Nunca una contraseña ni un hash.</param>
/// <param name="Origen">Dirección remota de la petición, si se conoce.</param>
public sealed record EventoDeAcceso(
    TipoDeEventoDeAcceso Tipo, string Usuario, string Actor, string? Detalle, string? Origen, DateTimeOffset Timestamp);

/// <summary>
/// Log de auditoría de accesos: append-only y separado de la cadena de custodia. Los eventos se
/// guardan con <see cref="IRepositorioDeUsuarios.GuardarCambiosAsync"/>, en la misma transacción que
/// el cambio que registran.
/// </summary>
public interface IAuditoriaDeAccesos
{
    void Registrar(EventoDeAcceso evento);
}

/// <summary>Argon2id. La contraseña nunca se guarda ni se loguea en claro.</summary>
public interface IHasheadorDeContrasena
{
    HashDeContrasena Hashear(ContrasenaAceptada contrasena);

    /// <summary>Compara en tiempo constante.</summary>
    bool Verificar(string contrasena, HashDeContrasena hash);

    /// <summary>
    /// Hash de una contraseña que nadie conoce, para verificar contra él cuando el usuario no existe:
    /// así un login con un usuario inexistente tarda lo mismo que uno con contraseña incorrecta.
    /// </summary>
    HashDeContrasena HashFicticio { get; }
}

public sealed record TokenEmitido(string Token, DateTimeOffset ExpiraEn);

/// <summary>Emite el token de acceso (JWT de vida corta) de un usuario ya autenticado.</summary>
public interface IEmisorDeTokens
{
    TokenEmitido Emitir(Actor actor);
}
