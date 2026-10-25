using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Forensic.Api.Controllers;
using Forensic.Application.Casos;
using Forensic.Application.Identidad;
using Forensic.Domain.Comun;
using Forensic.Infrastructure.Identidad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>
/// El camino real de identidad: login con contraseña, JWT, autorización por rol y por caso, lockout
/// y auditoría de accesos separada de la cadena de custodia.
/// </summary>
public class IdentidadTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private HttpClient Anonimo() => servidor.CreateClient();

    private static string NombreUnico(string prefijo) => $"{prefijo}-{Guid.NewGuid():N}"[..20];

    private string Token(DateTime emitido, DateTime expira, string clave = ServidorForense.ClaveDeFirma, string usuario = ServidorForense.AdminInicial) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "forensic-video",
            Audience = "forensic-video",
            IssuedAt = emitido,
            NotBefore = emitido,
            Expires = expira,
            Claims = new Dictionary<string, object> { [OpcionesJwt.ClaimUsuario] = usuario, [OpcionesJwt.ClaimRol] = "Admin" },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)), SecurityAlgorithms.HmacSha256),
        });

    private async Task<List<(string Tipo, string Usuario, string Actor, string? Detalle)>> AuditoriaDeAsync(string usuario)
    {
        await using var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """SELECT "Tipo", "Usuario", "Actor", "Detalle" FROM identidad."AuditoriaDeAccesos" WHERE "Usuario" = $1 ORDER BY "Id" """, conexion)
        {
            Parameters = { new() { Value = usuario } },
        };
        var filas = new List<(string, string, string, string?)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            filas.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return filas;
    }

    private async Task<long> AsientosDeCustodiaAsync()
    {
        await using var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();
        await using var cmd = new NpgsqlCommand("""SELECT count(*) FROM "AsientosDeCustodia" """, conexion);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<(string? Title, string? Detail)> ProblemaAsync(HttpResponseMessage r)
    {
        var problema = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        return (problema.GetProperty("title").GetString(), problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task El_login_emite_un_token_de_vida_corta_que_autoriza_contra_la_api()
    {
        var lucia = await servidor.UsuarioConSesionAsync(NombreUnico("lucia"), Rol.Analista);

        var r = await lucia.PostAsJsonAsync("/casos", new AbrirCasoRequest(NombreUnico("EXP"), "con JWT"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var token = await Anonimo().LoginAsync(ServidorForense.AdminInicial, ServidorForense.ContrasenaDelAdmin);
        var restante = token.ExpiraEn - DateTimeOffset.UtcNow;
        Assert.InRange(restante, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Con_la_contrasena_inicial_solo_se_puede_cambiar_la_contrasena()
    {
        var admin = await servidor.AdminAsync();
        var nombre = NombreUnico("ines");
        (await admin.PostAsJsonAsync("/usuarios", new CrearUsuarioRequest(nombre, Rol.Analista, "contrasena temporal 2026"), ServidorForense.Json))
            .EnsureSuccessStatusCode();
        var inicial = servidor.ClienteConToken((await Anonimo().LoginAsync(nombre, "contrasena temporal 2026")).Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await inicial.GetAsync("/casos")).StatusCode);

        var definitivo = servidor.ClienteConToken((await inicial.CambiarContrasenaAsync("contrasena temporal 2026", "contrasena definitiva 2026")).Token);
        Assert.Equal(HttpStatusCode.OK, (await definitivo.GetAsync("/casos")).StatusCode);
    }

    [Fact]
    public async Task La_autorizacion_por_caso_rige_con_el_actor_real_del_token()
    {
        var lucia = await servidor.UsuarioConSesionAsync(NombreUnico("lucia"), Rol.Analista);
        var mateo = await servidor.UsuarioConSesionAsync(NombreUnico("mateo"), Rol.Analista);
        var caso = await lucia.AbrirCasoAsync();

        Assert.Equal(HttpStatusCode.OK, (await lucia.GetAsync($"/casos/{caso.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mateo.GetAsync($"/casos/{caso.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await servidor.AdminAsync()).GetAsync($"/casos/{caso.Id}")).StatusCode);
        var export = await lucia.ExportarAsync(caso.Id);
        Assert.Equal(caso.AbiertoPor, export.Asientos[0].Actor);
        Assert.StartsWith("lucia-", export.Asientos[0].Actor);
    }

    [Fact]
    public async Task Sin_token_o_con_un_token_invalido_mal_firmado_o_vencido_es_401()
    {
        await servidor.AdminAsync();
        var ahora = DateTime.UtcNow;
        string[] tokens =
        [
            "no-es-un-jwt",
            Token(ahora, ahora.AddMinutes(15), clave: "otra-clave-de-firma-de-32-bytes-o-mas"),
            Token(ahora.AddHours(-2), ahora.AddHours(-1)),
        ];

        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().GetAsync("/casos")).StatusCode);
        foreach (var token in tokens)
            Assert.Equal(HttpStatusCode.Unauthorized, (await servidor.ClienteConToken(token).GetAsync("/casos")).StatusCode);
        // Control: el mismo armado con la clave correcta y vigente pasa.
        Assert.Equal(HttpStatusCode.OK, (await servidor.ClienteConToken(Token(ahora, ahora.AddMinutes(15))).GetAsync("/usuarios")).StatusCode);
    }

    [Fact]
    public async Task Los_endpoints_de_usuarios_rechazan_a_quien_no_es_admin()
    {
        var supervisora = await servidor.UsuarioConSesionAsync(NombreUnico("sofia"), Rol.Supervisor);
        var auditor = servidor.ClienteDe("aurelio", Rol.Auditor);
        var alta = new CrearUsuarioRequest(NombreUnico("x"), Rol.Analista, "contrasena temporal 2026");

        foreach (var cliente in new[] { supervisora, auditor })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/usuarios")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync("/usuarios", alta, ServidorForense.Json)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().GetAsync("/usuarios")).StatusCode);
    }

    [Fact]
    public async Task El_error_de_login_es_el_mismo_si_falla_el_usuario_o_la_contrasena()
    {
        var nombre = NombreUnico("tomas");
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista);

        var inexistente = await Anonimo().PostLoginAsync(NombreUnico("nadie"), "contrasena definitiva 2026");
        var incorrecta = await Anonimo().PostLoginAsync(nombre, "otra contrasena cualquiera");

        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, incorrecta.StatusCode);
        Assert.Equal(await ProblemaAsync(inexistente), await ProblemaAsync(incorrecta));
    }

    [Fact]
    public async Task Tras_cinco_intentos_fallidos_la_cuenta_se_bloquea_con_el_mismo_error_generico()
    {
        var nombre = NombreUnico("nora");
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista, "la contrasena de nora");
        var generico = await ProblemaAsync(await Anonimo().PostLoginAsync(NombreUnico("nadie"), "lo que sea 123"));

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().PostLoginAsync(nombre, "contrasena equivocada")).StatusCode);

        var bloqueada = await Anonimo().PostLoginAsync(nombre, "la contrasena de nora");
        Assert.Equal(HttpStatusCode.Unauthorized, bloqueada.StatusCode);
        Assert.Equal(generico, await ProblemaAsync(bloqueada));

        // El motivo real queda sólo en la auditoría de accesos.
        var auditoria = await AuditoriaDeAsync(nombre);
        Assert.Equal(5, auditoria.Count(e => e is { Tipo: "LoginFallido", Detalle: "ContrasenaIncorrecta" }));
        Assert.Single(auditoria, e => e.Tipo == "CuentaBloqueada");
        Assert.Equal(("LoginFallido", "Bloqueado"), (auditoria[^1].Tipo, auditoria[^1].Detalle));
    }

    [Fact]
    public async Task Deshabilitar_o_cambiar_el_rol_invalida_al_instante_los_tokens_vigentes()
    {
        var admin = await servidor.AdminAsync();
        var (rafa, rita) = (NombreUnico("rafa"), NombreUnico("rita"));
        var deRafa = await servidor.UsuarioConSesionAsync(rafa, Rol.Analista);
        var deRita = await servidor.UsuarioConSesionAsync(rita, Rol.Analista);
        Assert.Equal(HttpStatusCode.OK, (await deRafa.GetAsync("/casos")).StatusCode);

        (await admin.PostAsync($"/usuarios/{rafa}/deshabilitar", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/usuarios/{rita}/rol", new CambiarRolRequest(Rol.Supervisor), ServidorForense.Json)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await deRafa.GetAsync("/casos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await deRita.GetAsync("/casos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().PostLoginAsync(rafa, "contrasena definitiva 2026")).StatusCode);
        // Con el rol nuevo, un login nuevo funciona.
        Assert.Equal(HttpStatusCode.OK, (await Anonimo().PostLoginAsync(rita, "contrasena definitiva 2026")).StatusCode);
    }

    [Fact]
    public async Task Un_reset_obliga_a_cambiar_la_contrasena_temporal()
    {
        var admin = await servidor.AdminAsync();
        var nombre = NombreUnico("olga");
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista);

        (await admin.PostAsJsonAsync($"/usuarios/{nombre}/reset-contrasena", new ResetearContrasenaRequest("temporal del reset 1"))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().PostLoginAsync(nombre, "contrasena definitiva 2026")).StatusCode);
        var temporal = servidor.ClienteConToken((await Anonimo().LoginAsync(nombre, "temporal del reset 1")).Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await temporal.GetAsync("/casos")).StatusCode);
    }

    [Fact]
    public async Task Los_eventos_de_identidad_van_a_la_auditoria_de_accesos_y_nunca_a_la_custodia()
    {
        var admin = await servidor.AdminAsync();
        var asientosAntes = await AsientosDeCustodiaAsync();
        var nombre = NombreUnico("pia");

        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista);
        await Anonimo().PostLoginAsync(nombre, "contrasena equivocada");
        (await admin.PutAsJsonAsync($"/usuarios/{nombre}/rol", new CambiarRolRequest(Rol.Auditor), ServidorForense.Json)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/usuarios/{nombre}/reset-contrasena", new ResetearContrasenaRequest("temporal del reset 1"))).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/usuarios/{nombre}/deshabilitar", null)).EnsureSuccessStatusCode();

        Assert.Equal(asientosAntes, await AsientosDeCustodiaAsync());
        var tipos = (await AuditoriaDeAsync(nombre)).Select(e => e.Tipo).ToList();
        Assert.Equal(
            ["UsuarioCreado", "LoginExitoso", "ContrasenaCambiada", "LoginFallido", "RolCambiado", "ContrasenaReseteada", "UsuarioDeshabilitado"],
            tipos);
        Assert.All(await AuditoriaDeAsync(nombre), e => Assert.DoesNotContain("contrasena", e.Detalle ?? ""));
    }

    [Fact]
    public async Task La_auditoria_de_accesos_es_append_only()
    {
        await Anonimo().PostLoginAsync(NombreUnico("nadie"), "lo que sea 123");
        await using var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();

        foreach (var sql in new[]
        {
            """UPDATE identidad."AuditoriaDeAccesos" SET "Actor" = 'otro'""",
            """DELETE FROM identidad."AuditoriaDeAccesos" """,
            """TRUNCATE identidad."AuditoriaDeAccesos" """,
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conexion);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Contains("append-only", ex.MessageText);
        }
    }

    [Fact]
    public void El_servidor_no_arranca_sin_la_clave_de_firma()
    {
        using var sinClave = servidor.WithWebHostBuilder(b => b.UseSetting("Identidad:Jwt:ClaveDeFirma", ""));

        var ex = Assert.ThrowsAny<Exception>(() => sinClave.CreateClient());
        Assert.Contains("ClaveDeFirma", ex.ToString());
    }

    [Fact]
    public async Task El_servidor_no_arranca_sin_admin_ni_seed_del_primer_admin()
    {
        // Una base vacía, sin ningún admin.
        var baseVacia = $"vacia_{Guid.NewGuid():N}";
        await using (var conexion = new NpgsqlConnection(servidor.CadenaDeConexion))
        {
            await conexion.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {baseVacia}", conexion);
            await cmd.ExecuteNonQueryAsync();
        }
        var conexionVacia = new NpgsqlConnectionStringBuilder(servidor.CadenaDeConexion) { Database = baseVacia }.ConnectionString;
        using var sinSeed = servidor.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Forense", conexionVacia);
            b.UseSetting("Identidad:AdminInicial:Usuario", "");
            b.UseSetting("Identidad:AdminInicial:Contrasena", "");
        });

        var ex = Assert.ThrowsAny<Exception>(() => sinSeed.CreateClient());
        Assert.Contains("AdminInicial", ex.ToString());
    }

    /// <summary>El mismo servidor y la misma base, con un límite de login bajo y su propio estado.</summary>
    private (HttpClient DesdeA, HttpClient DesdeB, IDisposable Fabrica) ConLimiteDeLogin(int intentos)
    {
        var limitado = servidor.WithWebHostBuilder(b => b.UseSetting("Identidad:LimiteDeLogin:IntentosPorVentana", intentos.ToString()));
        var desdeA = limitado.CreateClient();
        desdeA.DefaultRequestHeaders.Add(IpDePrueba.Cabecera, "10.0.0.1");
        var desdeB = limitado.CreateClient();
        desdeB.DefaultRequestHeaders.Add(IpDePrueba.Cabecera, "10.0.0.2");
        return (desdeA, desdeB, limitado);
    }

    [Fact]
    public async Task Superado_el_limite_de_logins_por_ip_responde_429_aunque_la_contrasena_sea_correcta()
    {
        var nombre = NombreUnico("ivan");
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista, "la contrasena de ivan");
        var (desdeA, desdeB, fabrica) = ConLimiteDeLogin(3);
        using var _ = fabrica;

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await desdeA.PostLoginAsync(nombre, "contrasena equivocada")).StatusCode);
        var limitada = await desdeA.PostLoginAsync(nombre, "la contrasena de ivan");

        Assert.Equal(HttpStatusCode.TooManyRequests, limitada.StatusCode);
        Assert.True(limitada.Headers.RetryAfter is not null);
        // El límite es por IP: desde otra, el mismo usuario entra.
        Assert.Equal(HttpStatusCode.OK, (await desdeB.PostLoginAsync(nombre, "la contrasena de ivan")).StatusCode);
    }

    [Fact]
    public async Task Una_rafaga_de_logins_en_paralelo_desde_una_ip_no_pasa_del_limite()
    {
        // El lockout por cuenta es débil ante concurrencia: el límite por IP es la defensa primaria.
        var nombre = NombreUnico("rosa");
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista);
        var (desdeA, _, fabrica) = ConLimiteDeLogin(5);
        using var __ = fabrica;

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => desdeA.PostLoginAsync(nombre, $"intento paralelo {i:00}")));

        Assert.Equal(5, respuestas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(15, respuestas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        // Los rechazados por el límite ni siquiera llegan a verificar la contraseña.
        Assert.True((await AuditoriaDeAsync(nombre)).Count(e => e.Tipo == "LoginFallido") <= 5);
    }
}
