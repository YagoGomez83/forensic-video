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
        var llamadas = 0;
        // La primera GET /casos es la del login; la segunda llega con el token ya vencido.
        _servidor.En(HttpMethod.Post, "/auth/login", HttpStatusCode.OK, Token)
            .En(HttpMethod.Get, "/casos", _ => ++llamadas == 1
                ? ServidorFalso.Respuesta(HttpStatusCode.OK, Array.Empty<object>())
                : ServidorFalso.Problema(HttpStatusCode.Unauthorized, "No autenticado", "token vencido"));
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

    [Fact]
    public async Task El_reporte_del_worker_de_ia_se_reenvia_al_servidor_sin_tocarlo()
    {
        // Espacios, orden de claves y dígitos tal cual: el cliente no re-serializa lo que dijo el modelo.
        const string reporte = """{"analisis": "ia.deteccion/v1",  "hallazgos": [{"confianza": 0.8213145732879639, "crudo": {"b": 1, "a": 2}}]}""";
        var casoId = Guid.NewGuid();
        var evidenciaId = Guid.NewGuid();
        _servidor.En(HttpMethod.Post, $"/casos/{casoId}/evidencias/{evidenciaId}/analisis-ia", HttpStatusCode.Created, new
        {
            id = Guid.NewGuid(), evidenciaId, analisis = "ia.deteccion/v1", parametros = """{"umbralConfianzaMilesimas":500}""",
            modelo = new { id = "yolox-s", version = "0.1.1rc0", sha256 = new string('c', 64) }, entorno = "{}", actor = "lucia",
            registradoEn = DateTimeOffset.UtcNow, via = "SoloInvestigacion",
            hallazgos = new[]
            {
                new
                {
                    orden = 0, region = new { x = 1, y = 2, ancho = 3, alto = 4 }, clase = "car", textoLeido = (string?)null,
                    confianza = 0.8213145732879639m, crudo = """{"b": 1, "a": 2}""", via = "SoloInvestigacion",
                },
            },
        });

        var analisis = await Cliente().RegistrarAnalisisIaAsync(casoId, evidenciaId, reporte, CancellationToken.None);

        Assert.Equal(reporte, _servidor.Cuerpos.Single());
        Assert.Equal("application/json", _servidor.Peticiones.Single().Content!.Headers.ContentType!.MediaType);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, analisis.Via);
        var hallazgo = Assert.Single(analisis.Hallazgos);
        Assert.Equal((0.8213145732879639m, "car", new RegionDto(1, 2, 3, 4)), (hallazgo.Confianza, hallazgo.Clase, hallazgo.Region));
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, hallazgo.Via);
    }

    [Fact]
    public async Task Los_analisis_de_ia_de_una_evidencia_se_listan()
    {
        var casoId = Guid.NewGuid();
        var evidenciaId = Guid.NewGuid();
        _servidor.En(HttpMethod.Get, $"/casos/{casoId}/evidencias/{evidenciaId}/analisis-ia", HttpStatusCode.OK, Array.Empty<object>());

        var analisis = await Cliente().ListarAnalisisIaAsync(casoId, evidenciaId, CancellationToken.None);

        Assert.Empty(analisis);
    }
}
