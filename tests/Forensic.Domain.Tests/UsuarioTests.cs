using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

public class UsuarioTests
{
    private static readonly HashDeContrasena UnHash = HashDeContrasena.Parse("$argon2id$v=19$m=65536,t=2,p=1$c2FsdA$aGFzaA");
    private static readonly HashDeContrasena OtroHash = HashDeContrasena.Parse("$argon2id$v=19$m=65536,t=2,p=1$c2FsdA$b3Rybw");

    private static Usuario Beto() => Usuario.Crear("beto", Rol.Analista, UnHash, Admin, Ahora);

    [Fact]
    public void Un_usuario_nuevo_queda_activo_y_debe_cambiar_la_contrasena()
    {
        var beto = Beto();

        Assert.True(beto.Activo);
        Assert.True(beto.DebeCambiarContrasena);
        Assert.Equal(Rol.Analista, beto.Rol);
        Assert.Equal("adela", beto.CreadoPor);
    }

    [Fact]
    public void El_rol_es_obligatorio()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear("beto", default, UnHash, Admin, Ahora));
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear("beto", (Rol)99, UnHash, Admin, Ahora));
    }

    [Fact]
    public void El_hash_de_contrasena_es_obligatorio()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear("beto", Rol.Analista, null!, Admin, Ahora));
    }

    [Fact]
    public void Solo_un_admin_crea_usuarios()
    {
        Assert.Throws<AccesoDenegadoException>(() => Usuario.Crear("beto", Rol.Analista, UnHash, Supervisora, Ahora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con espacio")]
    [InlineData("a/b")]
    public void El_nombre_de_usuario_es_un_identificador_simple(string nombre)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear(nombre, Rol.Analista, UnHash, Admin, Ahora));
    }

    [Fact]
    public void Una_contrasena_correcta_autentica_y_limpia_los_intentos_fallidos()
    {
        var beto = Beto();
        beto.Autenticar(contrasenaValida: false, Ahora);

        Assert.Null(beto.Autenticar(contrasenaValida: true, Ahora));
        Assert.Equal(0, beto.IntentosFallidos);
    }

    [Fact]
    public void Un_usuario_deshabilitado_no_autentica_aunque_la_contrasena_sea_correcta()
    {
        var beto = Beto();
        beto.Deshabilitar(Admin);

        Assert.Equal(MotivoDeRechazo.Deshabilitado, beto.Autenticar(contrasenaValida: true, Ahora));
    }

    [Fact]
    public void Tras_el_quinto_intento_fallido_la_cuenta_se_bloquea_quince_minutos()
    {
        var beto = Beto();
        for (var i = 1; i < Usuario.MaximoDeIntentosFallidos; i++)
            Assert.Equal(MotivoDeRechazo.ContrasenaIncorrecta, beto.Autenticar(contrasenaValida: false, Ahora));
        Assert.Null(beto.BloqueadoHasta);

        Assert.Equal(MotivoDeRechazo.ContrasenaIncorrecta, beto.Autenticar(contrasenaValida: false, Ahora));

        Assert.Equal(Ahora + TimeSpan.FromMinutes(15), beto.BloqueadoHasta);
        // Bloqueada, ni la contraseña correcta entra.
        Assert.Equal(MotivoDeRechazo.Bloqueado, beto.Autenticar(contrasenaValida: true, Ahora.AddMinutes(14)));
        Assert.Null(beto.Autenticar(contrasenaValida: true, Ahora.AddMinutes(15)));
    }

    [Fact]
    public void Un_admin_no_se_deshabilita_ni_se_cambia_el_rol_a_si_mismo()
    {
        // Como el actor es un admin activo distinto del afectado, el sistema nunca se queda sin admin.
        var adela = Usuario.CrearAdminInicial("adela", UnHash, Ahora);

        Assert.Throws<ExcepcionDeDominio>(() => adela.Deshabilitar(Admin));
        Assert.Throws<ExcepcionDeDominio>(() => adela.CambiarRol(Rol.Supervisor, Admin));
    }

    [Fact]
    public void Solo_un_admin_cambia_roles_deshabilita_y_resetea()
    {
        var beto = Beto();

        Assert.Throws<AccesoDenegadoException>(() => beto.CambiarRol(Rol.Supervisor, Supervisora));
        Assert.Throws<AccesoDenegadoException>(() => beto.Deshabilitar(Supervisora));
        Assert.Throws<AccesoDenegadoException>(() => beto.ResetearContrasena(OtroHash, Supervisora));

        beto.CambiarRol(Rol.Supervisor, Admin);
        Assert.Equal(Rol.Supervisor, beto.Rol);
    }

    [Fact]
    public void Un_reset_obliga_a_cambiar_la_contrasena_y_desbloquea()
    {
        var beto = Beto();
        beto.CambiarContrasena(OtroHash, beto.ComoActor());
        for (var i = 0; i < Usuario.MaximoDeIntentosFallidos; i++)
            beto.Autenticar(contrasenaValida: false, Ahora);

        beto.ResetearContrasena(UnHash, Admin);

        Assert.True(beto.DebeCambiarContrasena);
        Assert.Null(beto.BloqueadoHasta);
        Assert.Equal(0, beto.IntentosFallidos);
        Assert.Equal(UnHash, beto.HashDeContrasena);
    }

    [Fact]
    public void Cada_uno_cambia_solo_su_propia_contrasena()
    {
        var beto = Beto();

        Assert.Throws<AccesoDenegadoException>(() => beto.CambiarContrasena(OtroHash, Ana));
        beto.CambiarContrasena(OtroHash, beto.ComoActor());

        Assert.False(beto.DebeCambiarContrasena);
        Assert.Equal(OtroHash, beto.HashDeContrasena);
    }

    [Fact]
    public void El_admin_inicial_es_admin_y_debe_cambiar_la_contrasena()
    {
        var adela = Usuario.CrearAdminInicial("adela", UnHash, Ahora);

        Assert.Equal(Rol.Admin, adela.Rol);
        Assert.True(adela.DebeCambiarContrasena);
        Assert.Equal(Usuario.Sistema, adela.CreadoPor);
    }

    [Fact]
    public void El_hash_no_se_muestra_al_convertirlo_en_texto_y_no_acepta_una_contrasena_en_claro()
    {
        Assert.DoesNotContain("argon2id", UnHash.ToString());
        Assert.Throws<ExcepcionDeDominio>(() => HashDeContrasena.Parse("contraseña-en-claro"));
    }
}
