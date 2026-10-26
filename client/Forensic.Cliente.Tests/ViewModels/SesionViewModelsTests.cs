using System.Net;
using System.Text.Json;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo;
using Forensic.Cliente.Nucleo.Sesion;
using Forensic.Cliente.Tests.Dobles;

namespace Forensic.Cliente.Tests.ViewModels;

public class SesionViewModelsTests
{
    private static object Token(string valor) => new { token = valor, expiraEn = DateTimeOffset.UtcNow.AddMinutes(15) };

    private readonly ServidorFalso _servidor = new();
    private readonly SesionEnMemoria _sesion = new();
    private readonly VentanaViewModel _ventana;

    public SesionViewModelsTests() =>
        _ventana = new VentanaViewModel(ClienteForense.Crear(new Uri("http://servidor/"), _sesion, _servidor));

    private LoginViewModel Login => Assert.IsType<LoginViewModel>(_ventana.Actual);

    [Fact]
    public void Al_abrir_se_muestra_el_login()
    {
        Assert.IsType<LoginViewModel>(_ventana.Actual);
    }

    [Fact]
    public async Task Con_credenciales_correctas_entra_a_los_casos()
    {
        _servidor.En(HttpMethod.Post, "/auth/login", HttpStatusCode.OK, Token("t1"))
            .En(HttpMethod.Get, "/casos", HttpStatusCode.OK, Array.Empty<object>());
        var login = Login;
        (login.Usuario, login.Contrasena) = ("lucia", "su contrasena");

        await login.IniciarCommand.ExecuteAsync(null);

        Assert.IsType<Nucleo.Casos.CasosViewModel>(_ventana.Actual);
        Assert.True(_sesion.Activa);
    }

    [Fact]
    public async Task Con_credenciales_malas_muestra_el_mensaje_del_servidor_y_sigue_en_el_login()
    {
        _servidor.En(HttpMethod.Post, "/auth/login", _ => ServidorFalso.Problema(HttpStatusCode.Unauthorized, "No autenticado", "Usuario o contraseña incorrectos."));
        var login = Login;
        (login.Usuario, login.Contrasena) = ("lucia", "mala");

        await login.IniciarCommand.ExecuteAsync(null);

        Assert.Same(login, _ventana.Actual);
        Assert.Equal("Usuario o contraseña incorrectos.", login.Error);
        Assert.Equal("", login.Contrasena);
    }

    [Fact]
    public async Task Con_la_contrasena_inicial_lleva_al_cambio_obligatorio()
    {
        // Opción (b): el servidor responde 403 a GET /casos mientras la contraseña sea temporal.
        _servidor.En(HttpMethod.Post, "/auth/login", HttpStatusCode.OK, Token("t1"))
            .En(HttpMethod.Get, "/casos", _ => ServidorFalso.Problema(HttpStatusCode.Forbidden, "Forbidden", "Forbidden"));
        var login = Login;
        (login.Usuario, login.Contrasena) = ("lucia", "temporal 2026");

        await login.IniciarCommand.ExecuteAsync(null);

        var cambio = Assert.IsType<CambioDeContrasenaViewModel>(_ventana.Actual);
        Assert.True(cambio.Obligatorio);
    }

    [Fact]
    public async Task El_cambio_con_una_confirmacion_distinta_no_llega_al_servidor()
    {
        var cambio = await CambioObligatorioAsync();
        (cambio.Nueva, cambio.Confirmacion) = ("frase definitiva 1", "frase definitiva 2");
        var peticiones = _servidor.Peticiones.Count;

        await cambio.CambiarCommand.ExecuteAsync(null);

        Assert.Equal(peticiones, _servidor.Peticiones.Count);
        Assert.NotNull(cambio.Error);
    }

    [Fact]
    public async Task Si_el_servidor_rechaza_la_contrasena_nueva_muestra_su_motivo()
    {
        var cambio = await CambioObligatorioAsync();
        _servidor.En(HttpMethod.Post, "/auth/contrasena", _ => ServidorFalso.Problema(HttpStatusCode.UnprocessableEntity, "Regla", "La contraseña debe tener al menos 12 caracteres."));
        (cambio.Nueva, cambio.Confirmacion) = ("corta", "corta");

        await cambio.CambiarCommand.ExecuteAsync(null);

        Assert.Same(cambio, _ventana.Actual);
        Assert.Equal("La contraseña debe tener al menos 12 caracteres.", cambio.Error);
    }

    [Fact]
    public async Task El_cambio_aceptado_usa_el_token_nuevo_y_entra_a_los_casos()
    {
        var cambio = await CambioObligatorioAsync();
        _servidor.En(HttpMethod.Post, "/auth/contrasena", HttpStatusCode.OK, Token("t2"));
        (cambio.Nueva, cambio.Confirmacion) = ("frase definitiva 1", "frase definitiva 1");

        await cambio.CambiarCommand.ExecuteAsync(null);

        Assert.Equal("t2", _sesion.Token);
        Assert.IsType<Nucleo.Casos.CasosViewModel>(_ventana.Actual);
        var cuerpo = JsonDocument.Parse(_servidor.Cuerpos[_servidor.Peticiones.FindLastIndex(p => p.RequestUri!.AbsolutePath == "/auth/contrasena")]!).RootElement;
        Assert.Equal(("temporal 2026", "frase definitiva 1"), (cuerpo.GetProperty("actual").GetString(), cuerpo.GetProperty("nueva").GetString()));
    }

    [Fact]
    public async Task Si_la_sesion_expira_vuelve_al_login_con_el_motivo()
    {
        await CambioObligatorioAsync();

        _sesion.Expirar();

        Assert.Equal("La sesión venció o dejó de ser válida. Volvé a iniciar sesión.", Login.Aviso);
    }

    [Fact]
    public async Task Con_el_servidor_inalcanzable_lo_dice_claramente()
    {
        _servidor.Inalcanzable = true;
        var login = Login;
        (login.Usuario, login.Contrasena) = ("lucia", "x");

        await login.IniciarCommand.ExecuteAsync(null);

        Assert.Contains("No se pudo conectar con el servidor", login.Error);
    }

    private async Task<CambioDeContrasenaViewModel> CambioObligatorioAsync()
    {
        _servidor.En(HttpMethod.Post, "/auth/login", HttpStatusCode.OK, Token("t1"))
            .En(HttpMethod.Get, "/casos", _ => _sesion.Token == "t1"
                ? ServidorFalso.Problema(HttpStatusCode.Forbidden, "Forbidden", "Forbidden")
                : ServidorFalso.Respuesta(HttpStatusCode.OK, Array.Empty<object>()));
        var login = Login;
        (login.Usuario, login.Contrasena) = ("lucia", "temporal 2026");
        await login.IniciarCommand.ExecuteAsync(null);
        return Assert.IsType<CambioDeContrasenaViewModel>(_ventana.Actual);
    }
}
