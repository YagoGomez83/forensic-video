using System.Net;
using Forensic.Application.Identidad;
using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;
using Forensic.Infrastructure.Identidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>
/// La auditoría de accesos encadenada por hash: la base encadena cada evento al insertarlo y el servidor
/// lo verifica con una implementación independiente. Un superusuario que saltee los triggers ya no
/// puede editarla ni borrarla sin dejar rastro.
/// </summary>
public sealed class AuditoriaDeAccesosEncadenadaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private const string Tabla = """identidad."AuditoriaDeAccesos" """;

    /// <summary>Las pruebas que manipulan la base usan un servidor propio: rompen la cadena a propósito.</summary>
    private static async Task<ServidorForense> ServidorPropioAsync()
    {
        var s = new ServidorForense();
        await s.InitializeAsync();
        return s;
    }

    private static async Task LoginsFallidosAsync(ServidorForense s, int cantidad)
    {
        for (var i = 0; i < cantidad; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.CreateClient().PostLoginAsync($"nadie-{i}", "una contraseña cualquiera")).StatusCode);
    }

    private static async Task<VerificacionDeAccesosDto> VerificarAsync(ServidorForense s) =>
        await (await s.ClienteDe("aurelio", Rol.Auditor).GetAsync("/auditoria-de-accesos/verificacion")).LeerAsync<VerificacionDeAccesosDto>();

    private static async Task SqlAsync(ServidorForense s, string sql)
    {
        await using var conexion = new NpgsqlConnection(s.CadenaDeConexion);
        await conexion.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conexion);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Un superusuario puede saltear el trigger append-only; el encadenado no lo protege de eso, lo delata.</summary>
    private static Task SinAppendOnlyAsync(ServidorForense s, string sql) => SqlAsync(s, $"""
        ALTER TABLE {Tabla} DISABLE TRIGGER "AuditoriaDeAccesos_append_only";
        {sql}
        ALTER TABLE {Tabla} ENABLE TRIGGER "AuditoriaDeAccesos_append_only";
        """);

    [Fact]
    public async Task Los_accesos_quedan_encadenados_por_la_base_y_verifican()
    {
        await LoginsFallidosAsync(servidor, 3);

        var v = await VerificarAsync(servidor);

        Assert.True(v.Verificacion.EsIntegra, v.Verificacion.Motivo);
        Assert.True(v.Verificacion.CantidadAsientos >= 3);
    }

    [Fact]
    public async Task Un_evento_insertado_a_mano_tambien_queda_encadenado()
    {
        await SqlAsync(servidor, $"""
            INSERT INTO {Tabla} ("Tipo", "Usuario", "Actor", "Detalle", "Origen", "Timestamp")
            VALUES ('LoginFallido', 'ñandú', 'ñandú', 'contraseña inválida', NULL, now());
            """);

        Assert.True((await VerificarAsync(servidor)).Verificacion.EsIntegra);
    }

    [Fact]
    public async Task Los_logins_concurrentes_no_rompen_ni_saltean_la_cadena()
    {
        var antes = (await VerificarAsync(servidor)).Verificacion.CantidadAsientos;

        await Task.WhenAll(Enumerable.Range(0, 30).Select(i =>
            servidor.CreateClient().PostLoginAsync($"concurrente-{i}", "una contraseña cualquiera")));

        var v = await VerificarAsync(servidor);
        Assert.True(v.Verificacion.EsIntegra, v.Verificacion.Motivo);
        Assert.Equal(antes + 30, v.Verificacion.CantidadAsientos);
    }

    [Fact]
    public async Task El_sellado_periodico_sella_tambien_la_auditoria_de_accesos()
    {
        await LoginsFallidosAsync(servidor, 1);

        await servidor.SellarPendientesAsync();

        var v = await VerificarAsync(servidor);
        Assert.True(v.Sellado.SonValidos, v.Sellado.Motivo);
        Assert.Equal(v.Verificacion.CantidadAsientos, v.Sellado.UltimaSecuenciaSellada);
    }

    [Fact]
    public async Task Ni_un_analista_ni_un_supervisor_ven_la_verificacion()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await servidor.ClienteDe("ana", Rol.Analista).GetAsync("/auditoria-de-accesos/verificacion")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await servidor.ClienteDe("sofia", Rol.Supervisor).GetAsync("/auditoria-de-accesos/verificacion")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await servidor.ClienteDe("adela", Rol.Admin).GetAsync("/auditoria-de-accesos/verificacion")).StatusCode);
    }

    [Fact]
    public async Task Editar_un_evento_salteando_los_triggers_se_detecta_en_ese_evento()
    {
        await using var s = await ServidorPropioAsync();
        await LoginsFallidosAsync(s, 4);

        await SinAppendOnlyAsync(s, $"""UPDATE {Tabla} SET "Usuario" = 'impostor' WHERE "Secuencia" = 2;""");

        var v = await VerificarAsync(s);
        Assert.False(v.Verificacion.EsIntegra);
        Assert.Equal(2, v.Verificacion.SecuenciaRota);
    }

    [Fact]
    public async Task Borrar_un_evento_intermedio_se_detecta()
    {
        await using var s = await ServidorPropioAsync();
        await LoginsFallidosAsync(s, 4);

        await SinAppendOnlyAsync(s, $"""DELETE FROM {Tabla} WHERE "Secuencia" = 2;""");

        var v = await VerificarAsync(s);
        Assert.False(v.Verificacion.EsIntegra);
        Assert.Equal(3, v.Verificacion.SecuenciaRota);
    }

    [Fact]
    public async Task Una_reconstruccion_total_verifica_por_si_misma_pero_la_delata_el_sello()
    {
        await using var s = await ServidorPropioAsync();
        await LoginsFallidosAsync(s, 4);
        await s.SellarPendientesAsync();

        // Se copia todo, se borra y se vuelve a insertar con un dato cambiado: el trigger la re-encadena entera.
        await SinAppendOnlyAsync(s, $"""
            CREATE TEMP TABLE copia AS SELECT * FROM {Tabla} ORDER BY "Secuencia";
            DELETE FROM {Tabla};
            INSERT INTO {Tabla} ("Tipo", "Usuario", "Actor", "Detalle", "Origen", "Timestamp")
                SELECT "Tipo", CASE WHEN "Secuencia" = 2 THEN 'impostor' ELSE "Usuario" END, "Actor", "Detalle", "Origen", "Timestamp"
                FROM copia ORDER BY "Secuencia";
            """);

        var v = await VerificarAsync(s);
        Assert.True(v.Verificacion.EsIntegra);
        Assert.False(v.Sellado.SonValidos);
        Assert.Contains("reconstruida", v.Sellado.Motivo);
    }

    [Fact]
    public async Task Truncar_el_final_lo_delata_el_sello()
    {
        await using var s = await ServidorPropioAsync();
        await LoginsFallidosAsync(s, 4);
        await s.SellarPendientesAsync();
        var cantidad = (await VerificarAsync(s)).Verificacion.CantidadAsientos;

        await SinAppendOnlyAsync(s, $"""DELETE FROM {Tabla} WHERE "Secuencia" > {cantidad - 2};""");

        var v = await VerificarAsync(s);
        Assert.True(v.Verificacion.EsIntegra);
        Assert.False(v.Sellado.SonValidos);
        Assert.Contains("truncada", v.Sellado.Motivo);
    }

    [Fact]
    public async Task La_migracion_encadena_los_eventos_que_ya_existian()
    {
        var baseLegada = $"legado_{Guid.NewGuid():N}";
        await SqlAsync(servidor, $"CREATE DATABASE {baseLegada};");
        var conexion = new NpgsqlConnectionStringBuilder(servidor.CadenaDeConexion) { Database = baseLegada }.ConnectionString;
        await using var db = new ContextoDeIdentidad(new DbContextOptionsBuilder<ContextoDeIdentidad>()
            .UseNpgsql(conexion, o => o.MigrationsHistoryTable("__EFMigrationsHistory", ContextoDeIdentidad.Schema)).Options);

        // La base tal como estaba antes de encadenar, con eventos ya registrados (acentos y nulos incluidos).
        var migrador = db.GetService<IMigrator>();
        await migrador.MigrateAsync("20261025214807_Identidad");
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO {Tabla} ("Tipo", "Usuario", "Actor", "Detalle", "Origen", "Timestamp") VALUES
              ('AdminInicialSembrado', 'adela', 'sistema', NULL, NULL, '2026-10-25 21:00:00.123456+00'),
              ('LoginFallido', 'ñandú', 'ñandú', 'contraseña inválida', '10.0.0.7', '2026-10-25 21:05:00.5+00'),
              ('LoginExitoso', 'adela', 'adela', '', '10.0.0.8', '2026-10-25 21:06:00+00');
            """);

        await migrador.MigrateAsync();

        var cadena = await new LectorDeAuditoriaDeAccesos(db).LeerCadenaAsync(CancellationToken.None);
        Assert.Equal(["AdminInicialSembrado", "LoginFallido", "LoginExitoso"], cadena.Select(a => a.Tipo));
        Assert.Equal([1L, 2, 3], cadena.Select(a => a.Secuencia));
        var v = CadenaDeAccesos.Verificar(cadena);
        Assert.True(v.EsIntegra, v.Motivo);

        // Y lo que llega después sigue la misma cadena.
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO {Tabla} ("Tipo", "Usuario", "Actor", "Detalle", "Origen", "Timestamp")
            VALUES ('LoginFallido', 'beto', 'beto', NULL, NULL, now());
            """);
        Assert.True(CadenaDeAccesos.Verificar(await new LectorDeAuditoriaDeAccesos(db).LeerCadenaAsync(CancellationToken.None)).EsIntegra);
    }
}
