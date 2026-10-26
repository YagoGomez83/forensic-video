using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Tests.Dobles;

namespace Forensic.Cliente.Tests.Api;

public class ClienteForenseTests
{
    private static readonly object Token = new { token = "jwt-de-prueba", expiraEn = DateTimeOffset.UtcNow.AddMinutes(15) };

    private readonly ServidorFalso _servidor = new();
    private readonly SesionEnMemoria _sesion = new();

    private ClienteForense Cliente() => ClienteForense.Crear(new Uri("http://servidor/"), _sesion, _servidor);

    [Fact]
    public async Task El_login_guarda_el_token_en_memoria_y_lo_envia_en_las_peticiones_siguientes()
    {
        _servidor.En(HttpMethod.Post, "/auth/login", HttpStatusCode.OK, Token)
            .En(HttpMethod.Get, "/casos", HttpStatusCode.OK, Array.Empty<object>());
        var cliente = Cliente();

        await cliente.IniciarSesionAsync("lucia", "su contrasena", CancellationToken.None);
        await cliente.ListarCasosAsync(CancellationToken.None);

        Assert.True(_sesion.Activa);
        Assert.Null(_servidor.Peticiones[0].Headers.Authorization);
        Assert.Equal(("Bearer", "jwt-de-prueba"), (_servidor.Peticiones[^1].Headers.Authorization!.Scheme, _servidor.Peticiones[^1].Headers.Authorization!.Parameter));
    }

    [Fact]
    public async Task Un_401_con_sesion_activa_la_expira_avisa_y_lanza_sesion_expirada()
    {
        _servidor.En(HttpMethod.Post, "/auth/login", HttpStatusCode.OK, Token)
            .En(HttpMethod.Get, "/casos", _ => ServidorFalso.Problema(HttpStatusCode.Unauthorized, "No autenticado", "token vencido"));
        var cliente = Cliente();
        await cliente.IniciarSesionAsync("lucia", "su contrasena", CancellationToken.None);
        var avisos = 0;
        _sesion.Expirada += (_, _) => avisos++;

        await Assert.ThrowsAsync<SesionExpiradaException>(() => cliente.ListarCasosAsync(CancellationToken.None));

        Assert.False(_sesion.Activa);
        Assert.Null(_sesion.Token);
        Assert.Equal(1, avisos);
    }

    [Fact]
    public async Task Un_login_rechazado_muestra_el_mensaje_generico_del_servidor_sin_expirar_nada()
    {
        _servidor.En(HttpMethod.Post, "/auth/login", _ => ServidorFalso.Problema(HttpStatusCode.Unauthorized, "No autenticado", "Usuario o contraseña incorrectos."));
        var avisos = 0;
        _sesion.Expirada += (_, _) => avisos++;

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Cliente().IniciarSesionAsync("lucia", "mala", CancellationToken.None));

        Assert.Equal(TipoDeError.CredencialesInvalidas, ex.Tipo);
        Assert.Equal("Usuario o contraseña incorrectos.", ex.Message);
        Assert.Equal(0, avisos);
        Assert.False(_sesion.Activa);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, TipoDeError.AccesoDenegado)]
    [InlineData(HttpStatusCode.NotFound, TipoDeError.NoEncontrado)]
    [InlineData(HttpStatusCode.Conflict, TipoDeError.Conflicto)]
    [InlineData(HttpStatusCode.UnprocessableEntity, TipoDeError.Rechazado)]
    [InlineData(HttpStatusCode.Locked, TipoDeError.IntegridadComprometida)]
    [InlineData(HttpStatusCode.TooManyRequests, TipoDeError.DemasiadosIntentos)]
    [InlineData(HttpStatusCode.BadGateway, TipoDeError.DivergenciaDeSeguridad)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TipoDeError.WorkerNoDisponible)]
    [InlineData(HttpStatusCode.InternalServerError, TipoDeError.ErrorDelServidor)]
    public async Task Los_problem_details_del_servidor_se_traducen_a_errores_tipados_con_su_detalle(HttpStatusCode estado, TipoDeError tipo)
    {
        _servidor.En(HttpMethod.Get, "/casos", _ => ServidorFalso.Problema(estado, "Título", "el detalle del servidor"));

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Cliente().ListarCasosAsync(CancellationToken.None));

        Assert.Equal((tipo, estado, "el detalle del servidor"), (ex.Tipo, ex.Estado, ex.Message));
    }

    [Fact]
    public async Task Un_servidor_inalcanzable_es_un_error_de_conexion_claro()
    {
        _servidor.Inalcanzable = true;

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => Cliente().ListarCasosAsync(CancellationToken.None));

        Assert.Equal(TipoDeError.ServidorInalcanzable, ex.Tipo);
        Assert.Contains("http://servidor/", ex.Message);
    }

    [Fact]
    public void El_token_nunca_aparece_al_convertir_la_sesion_en_texto()
    {
        _sesion.Iniciar("jwt-secreto", DateTimeOffset.UtcNow.AddMinutes(15));

        Assert.DoesNotContain("jwt-secreto", _sesion.ToString());
    }
}
