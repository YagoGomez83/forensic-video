using System.Security.Cryptography;
using Forensic.Application;
using Forensic.Application.Evidencias;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Forensic.IntegrationTests;

/// <summary>
/// Un análisis de IA se guarda junto con su asiento <see cref="TipoAsiento.HallazgosRegistrados"/> en una
/// sola transacción, se relee exacto y la base lo protege igual que a la custodia: append-only, y un
/// hallazgo no puede figurar como admisible ni escribiéndolo a mano.
/// </summary>
public class PersistenciaDeAnalisisIaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private const string Crudo = """{"bbox": [10.4, 20.7, 50.2, 30.9], "clase": 2, "score": 0.82131439}""";
    private const string Entorno = """{"onnxruntime":"1.31.0","gpu":null}""";

    private static readonly Actor Ana = new("ana", Rol.Analista);

    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    private async Task<(Guid CasoId, EvidenciaDto Original)> CasoConOriginalAsync()
    {
        var caso = await _ana.AbrirCasoAsync();
        var original = await (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(4096), "cam1.png"))
            .LeerAsync<EvidenciaDto>();
        return (caso.Id, original);
    }

    private async Task<AnalisisIa> RegistrarAsync(Guid casoId, Guid evidenciaId, IReadOnlyList<HallazgoPropuesto> hallazgos)
    {
        using var scope = servidor.Services.CreateScope();
        var persistencia = scope.ServiceProvider.GetRequiredService<IPersistenciaForense>();
        var caso = (await persistencia.ObtenerCasoAsync(casoId, default))!;
        var evidencia = (await persistencia.ObtenerEvidenciaAsync(casoId, evidenciaId, default))!;
        var definicion = CatalogoDeIa.Interpretar(
            CatalogoDeIa.Deteccion, System.Text.Json.JsonDocument.Parse("""{"umbralConfianzaMilesimas":500}""").RootElement);
        var analisis = AnalisisIa.Registrar(
            caso, evidencia, definicion, CatalogoDeIa.Modelos(CatalogoDeIa.Deteccion)[0], Entorno, evidencia.Hash,
            hallazgos, Ana, DateTimeOffset.UtcNow);
        var ultimo = await persistencia.ObtenerUltimoAsientoAsync(casoId, default);
        persistencia.Agregar(analisis);
        persistencia.Agregar(RegistroDeCustodia.Asentar(
            ultimo, casoId, TipoAsiento.HallazgosRegistrados, evidenciaId, analisis.DetalleDeAsiento(), Ana, analisis.RegistradoEn));
        await persistencia.GuardarCambiosAsync(default);
        return analisis;
    }

    private static HallazgoPropuesto Auto(decimal confianza) =>
        HallazgoPropuesto.Deteccion(RegionDeImagen.Crear(10, 20, 50, 30), "car", confianza, Crudo);

    private async Task<NpgsqlConnection> ConexionAsync()
    {
        var conexion = new NpgsqlConnection(servidor.CadenaDeConexion);
        await conexion.OpenAsync();
        return conexion;
    }

    [Fact]
    public async Task Se_guarda_con_sus_hallazgos_y_su_asiento_y_se_relee_exacto()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var leido = HallazgoPropuesto.Texto(RegionDeImagen.Crear(1, 2, 80, 20), "ABC123", 0.82m, """{"texto":"ABC123"}""");
        var registrado = await RegistrarAsync(casoId, original.Id, [Auto(0.82131439m), leido]);

        using var scope = servidor.Services.CreateScope();
        var persistencia = scope.ServiceProvider.GetRequiredService<IPersistenciaForense>();
        var analisis = Assert.Single(await persistencia.ListarAnalisisIaAsync(casoId, original.Id, default));

        Assert.Equal(registrado.Id, analisis.Id);
        Assert.Equal(CatalogoDeIa.Modelos(CatalogoDeIa.Deteccion)[0], analisis.Modelo);
        Assert.Equal(original.Hash, analisis.HashEntrada.Hex);
        Assert.Equal("""{"umbralConfianzaMilesimas":500}""", analisis.Parametros);
        Assert.Equal(Entorno, analisis.Entorno);
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, analisis.Via);
        Assert.Collection(
            analisis.Hallazgos,
            h =>
            {
                Assert.Equal("car", h.Clase);
                Assert.Equal(0.82131439m, h.Confianza);
                Assert.Equal(Crudo, h.DatoCrudo);
                Assert.Equal(RegionDeImagen.Crear(10, 20, 50, 30), h.Region);
            },
            h =>
            {
                Assert.Equal("ABC123", h.TextoLeido);
                Assert.Null(h.Clase);
                Assert.Equal(0.82m, h.Confianza);
            });

        var cadena = await persistencia.ObtenerCadenaAsync(casoId, default);
        Assert.Equal(TipoAsiento.HallazgosRegistrados, cadena[^1].Tipo);
        Assert.Equal(registrado.DetalleDeAsiento(), cadena[^1].Detalle);
        Assert.True(RegistroDeCustodia.Verificar(casoId, cadena).EsIntegra);
    }

    [Fact]
    public async Task Un_analisis_sin_hallazgos_se_guarda_igual()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        await RegistrarAsync(casoId, original.Id, []);

        using var scope = servidor.Services.CreateScope();
        var analisis = Assert.Single(await scope.ServiceProvider.GetRequiredService<IPersistenciaForense>()
            .ListarAnalisisIaAsync(casoId, original.Id, default));
        Assert.Empty(analisis.Hallazgos);
    }

    [Theory]
    [InlineData("""UPDATE "AnalisisIa" SET "Parametros" = '{}'""")]
    [InlineData("""DELETE FROM "AnalisisIa" """)]
    [InlineData("""TRUNCATE "AnalisisIa" CASCADE""")]
    [InlineData("""UPDATE "HallazgosIa" SET "Confianza" = 0.99""")]
    [InlineData("""UPDATE "HallazgosIa" SET "Via" = 'AdmisibleEnJuicio'""")]
    [InlineData("""DELETE FROM "HallazgosIa" """)]
    [InlineData("""TRUNCATE "HallazgosIa" """)]
    public async Task Los_analisis_y_sus_hallazgos_son_append_only(string sql)
    {
        var (casoId, original) = await CasoConOriginalAsync();
        await RegistrarAsync(casoId, original.Id, [Auto(0.9m)]);
        await using var conexion = await ConexionAsync();

        await using var cmd = new NpgsqlCommand(sql, conexion);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Contains("append-only", ex.MessageText);
    }

    [Theory]
    [InlineData("HallazgosIa")]
    [InlineData("AnalisisIa")]
    public async Task La_base_rechaza_escribir_a_mano_algo_de_ia_como_admisible(string tabla)
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var analisis = await RegistrarAsync(casoId, original.Id, [Auto(0.9m)]);
        await using var conexion = await ConexionAsync();

        // Copia de una fila existente con otro id y la vía cambiada: el CHECK la rechaza.
        var columnas = await ColumnasAsync(conexion, tabla);
        var select = string.Join(", ", columnas.Select(c => c switch
        {
            "Id" => "gen_random_uuid()",
            "Via" => "'AdmisibleEnJuicio'",
            _ => $"\"{c}\"",
        }));
        var filtro = tabla == "AnalisisIa" ? "\"Id\"" : "\"AnalisisId\"";
        await using var cmd = new NpgsqlCommand(
            $"""INSERT INTO "{tabla}" ({string.Join(", ", columnas.Select(c => $"\"{c}\""))}) SELECT {select} FROM "{tabla}" WHERE {filtro} = @id""",
            conexion);
        cmd.Parameters.AddWithValue("id", analisis.Id);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task La_via_por_defecto_en_la_base_es_solo_investigacion()
    {
        var (casoId, original) = await CasoConOriginalAsync();
        var analisis = await RegistrarAsync(casoId, original.Id, [Auto(0.9m)]);
        await using var conexion = await ConexionAsync();

        await using var cmd = new NpgsqlCommand(
            """SELECT a."Via", h."Via" FROM "AnalisisIa" a JOIN "HallazgosIa" h ON h."AnalisisId" = a."Id" WHERE a."Id" = @id""",
            conexion);
        cmd.Parameters.AddWithValue("id", analisis.Id);
        await using var lector = await cmd.ExecuteReaderAsync();

        Assert.True(await lector.ReadAsync());
        Assert.Equal(("SoloInvestigacion", "SoloInvestigacion"), (lector.GetString(0), lector.GetString(1)));
    }

    private static async Task<List<string>> ColumnasAsync(NpgsqlConnection conexion, string tabla)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = @tabla ORDER BY ordinal_position", conexion);
        cmd.Parameters.AddWithValue("tabla", tabla);
        var columnas = new List<string>();
        await using var lector = await cmd.ExecuteReaderAsync();
        while (await lector.ReadAsync())
            columnas.Add(lector.GetString(0));
        return columnas;
    }
}
