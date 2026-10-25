using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;

namespace Forensic.Application.Identidad;

public sealed record UsuarioDto(
    string Nombre, Rol Rol, bool Activo, bool DebeCambiarContrasena, DateTimeOffset? BloqueadoHasta,
    DateTimeOffset CreadoEn, string CreadoPor)
{
    internal static UsuarioDto Desde(Usuario u) =>
        new(u.Nombre, u.Rol, u.Activo, u.DebeCambiarContrasena, u.BloqueadoHasta, u.CreadoEn, u.CreadoPor);
}

/// <summary>Estado vigente de un usuario, para validar un token en cada petición.</summary>
public sealed record EstadoDeSesion(Rol Rol, bool DebeCambiarContrasena);

/// <summary>
/// Login y administración de usuarios. Cada evento va a la auditoría de accesos, nunca a la cadena
/// de custodia.
/// </summary>
public sealed class ServicioDeIdentidad(
    IRepositorioDeUsuarios usuarios,
    IAuditoriaDeAccesos auditoria,
    IHasheadorDeContrasena hasheador,
    IEmisorDeTokens emisor,
    IActorActual actorActual,
    TimeProvider reloj)
{
    /// <summary>
    /// Autentica y emite un token. Cualquier rechazo (usuario inexistente, contraseña incorrecta,
    /// cuenta bloqueada o deshabilitada) lanza la misma <see cref="CredencialesInvalidasException"/>;
    /// el motivo real queda sólo en la auditoría.
    /// </summary>
    public async Task<TokenEmitido> AutenticarAsync(string nombre, string contrasena, string? origen, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerAsync(nombre ?? "", ct);
        // Siempre se verifica un hash, exista o no el usuario: el tiempo de respuesta no lo delata.
        var valida = hasheador.Verificar(contrasena ?? "", usuario?.HashDeContrasena ?? hasheador.HashFicticio);
        var ahora = reloj.GetUtcNow();

        if (usuario is null)
        {
            await RechazarAsync(nombre ?? "", MotivoDeRechazo.UsuarioInexistente, bloqueada: false, origen, ahora, ct);
            throw new CredencialesInvalidasException();
        }

        var estabaBloqueado = usuario.BloqueadoHasta is { } hasta && ahora < hasta;
        var motivo = usuario.Autenticar(valida, ahora);
        if (motivo is not null)
        {
            var seBloqueo = !estabaBloqueado && usuario.BloqueadoHasta is { } nuevo && ahora < nuevo;
            await RechazarAsync(usuario.Nombre, motivo.Value, seBloqueo, origen, ahora, ct);
            throw new CredencialesInvalidasException();
        }

        auditoria.Registrar(new EventoDeAcceso(TipoDeEventoDeAcceso.LoginExitoso, usuario.Nombre, usuario.Nombre, null, origen, ahora));
        await GuardarLoginAsync(ct);
        return emisor.Emitir(usuario.ComoActor());
    }

    /// <summary>
    /// Cambio de la propia contraseña, verificando la actual. Un fallo cuenta como intento fallido.
    /// Devuelve un token nuevo, ya sin la obligación de cambiarla.
    /// </summary>
    public async Task<TokenEmitido> CambiarContrasenaAsync(string actual, string nueva, CancellationToken ct)
    {
        var actor = actorActual.Actor;
        var usuario = await usuarios.ObtenerAsync(actor.Usuario, ct)
            ?? throw new CredencialesInvalidasException();
        var ahora = reloj.GetUtcNow();

        var motivo = usuario.Autenticar(hasheador.Verificar(actual ?? "", usuario.HashDeContrasena), ahora);
        if (motivo is not null)
        {
            auditoria.Registrar(new EventoDeAcceso(
                TipoDeEventoDeAcceso.LoginFallido, usuario.Nombre, actor.Usuario, $"cambio de contraseña: {motivo}", null, ahora));
            await usuarios.GuardarCambiosAsync(ct);
            throw new ExcepcionDeDominio("La contraseña actual no es correcta.");
        }

        var aceptada = PoliticaDeContrasena.Validar(nueva, usuario.Nombre);
        if (hasheador.Verificar(aceptada.Valor, usuario.HashDeContrasena))
            throw new ExcepcionDeDominio("La contraseña nueva debe ser distinta de la actual.");
        usuario.CambiarContrasena(hasheador.Hashear(aceptada), actor);

        auditoria.Registrar(new EventoDeAcceso(TipoDeEventoDeAcceso.ContrasenaCambiada, usuario.Nombre, actor.Usuario, null, null, ahora));
        await usuarios.GuardarCambiosAsync(ct);
        return emisor.Emitir(usuario.ComoActor());
    }

    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct)
    {
        ExigirAdmin();
        return (await usuarios.ListarAsync(ct)).Select(UsuarioDto.Desde).ToList();
    }

    public async Task<UsuarioDto> CrearAsync(string nombre, Rol rol, string contrasenaInicial, CancellationToken ct)
    {
        var admin = ExigirAdmin();
        if (await usuarios.ObtenerAsync(nombre ?? "", ct) is not null)
            throw new ExcepcionDeDominio($"Ya existe el usuario '{nombre}'.");

        var aceptada = PoliticaDeContrasena.Validar(contrasenaInicial, nombre ?? "");
        var usuario = Usuario.Crear(nombre!, rol, hasheador.Hashear(aceptada), admin, reloj.GetUtcNow());

        usuarios.Agregar(usuario);
        await AuditarAsync(TipoDeEventoDeAcceso.UsuarioCreado, usuario, admin, $"rol {usuario.Rol}", ct);
        return UsuarioDto.Desde(usuario);
    }

    public async Task<UsuarioDto> CambiarRolAsync(string nombre, Rol rol, CancellationToken ct)
    {
        var admin = ExigirAdmin();
        var usuario = await CargarAsync(nombre, ct);
        var anterior = usuario.Rol;
        usuario.CambiarRol(rol, admin);

        await AuditarAsync(TipoDeEventoDeAcceso.RolCambiado, usuario, admin, $"{anterior} → {usuario.Rol}", ct);
        return UsuarioDto.Desde(usuario);
    }

    public async Task<UsuarioDto> ResetearContrasenaAsync(string nombre, string contrasenaTemporal, CancellationToken ct)
    {
        var admin = ExigirAdmin();
        var usuario = await CargarAsync(nombre, ct);
        var aceptada = PoliticaDeContrasena.Validar(contrasenaTemporal, usuario.Nombre);
        usuario.ResetearContrasena(hasheador.Hashear(aceptada), admin);

        await AuditarAsync(TipoDeEventoDeAcceso.ContrasenaReseteada, usuario, admin, null, ct);
        return UsuarioDto.Desde(usuario);
    }

    public async Task<UsuarioDto> DeshabilitarAsync(string nombre, CancellationToken ct)
    {
        var admin = ExigirAdmin();
        var usuario = await CargarAsync(nombre, ct);
        usuario.Deshabilitar(admin);

        await AuditarAsync(TipoDeEventoDeAcceso.UsuarioDeshabilitado, usuario, admin, null, ct);
        return UsuarioDto.Desde(usuario);
    }

    /// <summary>
    /// Siembra el primer admin si todavía no hay ninguno activo. Devuelve false si ya existía (el
    /// seed se ignora).
    /// </summary>
    public async Task<bool> SembrarAdminInicialAsync(string nombre, string contrasena, CancellationToken ct)
    {
        if (await usuarios.ExisteAdminActivoAsync(ct))
            return false;
        var aceptada = PoliticaDeContrasena.Validar(contrasena, nombre);
        var admin = Usuario.CrearAdminInicial(nombre, hasheador.Hashear(aceptada), reloj.GetUtcNow());

        usuarios.Agregar(admin);
        await AuditarAsync(TipoDeEventoDeAcceso.AdminInicialSembrado, admin, new Actor(Usuario.Sistema, Rol.Admin), null, ct);
        return true;
    }

    public Task<bool> ExisteAdminActivoAsync(CancellationToken ct) => usuarios.ExisteAdminActivoAsync(ct);

    /// <summary>
    /// Estado vigente del usuario de un token, o null si ya no puede operar (deshabilitado o
    /// inexistente). Se consulta en cada petición: deshabilitar o cambiar un rol rige al instante.
    /// </summary>
    public async Task<EstadoDeSesion?> ObtenerEstadoDeSesionAsync(string nombre, CancellationToken ct) =>
        await usuarios.ObtenerAsync(nombre, ct) is { Activo: true } u ? new EstadoDeSesion(u.Rol, u.DebeCambiarContrasena) : null;

    private async Task RechazarAsync(
        string nombre, MotivoDeRechazo motivo, bool bloqueada, string? origen, DateTimeOffset ahora, CancellationToken ct)
    {
        auditoria.Registrar(new EventoDeAcceso(TipoDeEventoDeAcceso.LoginFallido, nombre, nombre, motivo.ToString(), origen, ahora));
        if (bloqueada)
            auditoria.Registrar(new EventoDeAcceso(
                TipoDeEventoDeAcceso.CuentaBloqueada, nombre, nombre, $"{Usuario.MaximoDeIntentosFallidos} intentos fallidos", origen, ahora));
        await GuardarLoginAsync(ct);
    }

    /// <summary>
    /// Un login que choca con otro concurrente sobre el mismo usuario se rechaza como cualquier otro:
    /// no autentica ni revela nada, y el intento que ganó quedó contado.
    /// </summary>
    private async Task GuardarLoginAsync(CancellationToken ct)
    {
        try
        {
            await usuarios.GuardarCambiosAsync(ct);
        }
        catch (ConflictoDeIdentidadException ex)
        {
            throw new CredencialesInvalidasException(ex);
        }
    }

    private async Task AuditarAsync(TipoDeEventoDeAcceso tipo, Usuario usuario, Actor actor, string? detalle, CancellationToken ct)
    {
        auditoria.Registrar(new EventoDeAcceso(tipo, usuario.Nombre, actor.Usuario, detalle, null, reloj.GetUtcNow()));
        await usuarios.GuardarCambiosAsync(ct);
    }

    private Actor ExigirAdmin()
    {
        var actor = actorActual.Actor;
        if (actor.Rol != Rol.Admin)
            throw new AccesoDenegadoException("Sólo un admin administra usuarios.");
        return actor;
    }

    private async Task<Usuario> CargarAsync(string nombre, CancellationToken ct) =>
        await usuarios.ObtenerAsync(nombre, ct)
        ?? throw new NoEncontradoException($"No existe el usuario '{nombre}'.");
}
