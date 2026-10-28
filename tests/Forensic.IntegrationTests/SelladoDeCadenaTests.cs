using System.Security.Cryptography;
using Forensic.Application.Custodia;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sodium;

namespace Forensic.IntegrationTests;

/// <summary>Sellado periódico de la cadena: un sello firmado con la clave del laboratorio, fuera de la base.</summary>
public class SelladoDeCadenaTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private readonly HttpClient _ana = servidor.ClienteDe("ana", Rol.Analista);

    private async Task<Guid> CasoConEvidenciasAsync(int evidencias)
    {
        var caso = await _ana.AbrirCasoAsync();
        for (var i = 0; i < evidencias; i++)
            (await _ana.IngestarAsync(caso.Id, RandomNumberGenerator.GetBytes(64))).EnsureSuccessStatusCode();
        return caso.Id;
    }

    private async Task<IReadOnlyList<SelloDeCadena>> SellosDeAsync(Guid casoId)
    {
        using var scope = servidor.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAlmacenDeSellos>().ListarAsync(casoId, CancellationToken.None);
    }

    [Fact]
    public async Task Sellar_pendientes_sella_la_cabeza_de_cada_caso_una_sola_vez()
    {
        var uno = await CasoConEvidenciasAsync(1);
        var dos = await CasoConEvidenciasAsync(2);

        await servidor.SellarPendientesAsync();
        await servidor.SellarPendientesAsync();

        Assert.Equal(2, Assert.Single(await SellosDeAsync(uno)).Secuencia);
        Assert.Equal(3, Assert.Single(await SellosDeAsync(dos)).Secuencia);
    }

    [Fact]
    public async Task Un_caso_con_asientos_nuevos_recibe_otro_sello_en_su_nueva_cabeza()
    {
        var caso = await CasoConEvidenciasAsync(1);
        await servidor.SellarPendientesAsync();

        (await _ana.IngestarAsync(caso, RandomNumberGenerator.GetBytes(64))).EnsureSuccessStatusCode();
        await servidor.SellarPendientesAsync();

        Assert.Equal(new long[] { 2, 3 }, (await SellosDeAsync(caso)).Select(s => s.Secuencia));
    }

    [Fact]
    public async Task El_export_trae_los_sellos_y_su_cruce_con_la_cadena()
    {
        var caso = await CasoConEvidenciasAsync(1);
        await servidor.SellarPendientesAsync();
        (await _ana.IngestarAsync(caso, RandomNumberGenerator.GetBytes(64))).EnsureSuccessStatusCode();

        var export = await _ana.ExportarAsync(caso);

        Assert.True(export.Sellado.Verificacion.SonValidos, export.Sellado.Verificacion.Motivo);
        Assert.Equal(2, export.Sellado.Verificacion.UltimaSecuenciaSellada);
        Assert.Equal(1, export.Sellado.Verificacion.AsientosSinSellar);
        var sello = Assert.Single(export.Sellado.Sellos);
        Assert.Equal(export.Asientos[1].Hash, sello.HashCabeza);
        Assert.Equal(export.Sellado.ClaveDelLaboratorio, sello.ClaveId);
    }

    [Fact]
    public async Task El_sello_del_export_verifica_con_la_clave_publica_publicada()
    {
        var caso = await CasoConEvidenciasAsync(1);
        await servidor.SellarPendientesAsync();

        var export = await _ana.ExportarAsync(caso);
        var s = Assert.Single(export.Sellado.Sellos);
        var publica = Convert.FromHexString(export.Sellado.ClavePublica);
        var mensaje = SelloDeCadena.Rehidratar(
            caso, s.Secuencia, HashSha256.Parse(s.HashCabeza), s.SelladoEn, s.ClaveId, []).MensajeCanonico();

        Assert.True(PublicKeyAuth.VerifyDetached(Convert.FromBase64String(s.Firma), mensaje, publica));
        Assert.Equal("ed25519:" + Convert.ToHexString(SHA256.HashData(publica)).ToLowerInvariant(), s.ClaveId);
    }

    [Fact]
    public async Task Una_cadena_reconstruida_en_la_base_verifica_pero_el_sello_la_delata()
    {
        var caso = await CasoConEvidenciasAsync(2);
        await servidor.SellarPendientesAsync();

        await servidor.ReconstruirCadenaAsync(caso);
        var export = await _ana.ExportarAsync(caso);

        Assert.True(export.Verificacion.EsIntegra);
        Assert.False(export.Sellado.Verificacion.SonValidos);
        Assert.Equal(3, export.Sellado.Verificacion.SecuenciaRota);
        Assert.Contains("reconstruida", export.Sellado.Verificacion.Motivo);
    }

    [Fact]
    public async Task Truncar_el_final_de_la_cadena_en_la_base_lo_delata_el_sello()
    {
        var caso = await CasoConEvidenciasAsync(3);
        await servidor.SellarPendientesAsync();

        await servidor.EjecutarSinTriggersAsync("""DELETE FROM "AsientosDeCustodia" WHERE "CasoId" = @caso AND "Secuencia" > 2;""", caso);
        var export = await _ana.ExportarAsync(caso);

        Assert.True(export.Verificacion.EsIntegra);
        Assert.False(export.Sellado.Verificacion.SonValidos);
        Assert.Contains("truncada", export.Sellado.Verificacion.Motivo);
    }

    [Fact]
    public async Task Una_cadena_comprometida_no_se_sella()
    {
        var caso = await CasoConEvidenciasAsync(1);
        await servidor.EjecutarSinTriggersAsync(
            """UPDATE "AsientosDeCustodia" SET "Actor" = 'impostor' WHERE "CasoId" = @caso AND "Secuencia" = 2;""", caso);

        await servidor.SellarPendientesAsync();

        Assert.Empty(await SellosDeAsync(caso));
    }

    [Fact]
    public async Task Una_cadena_cuyos_sellos_ya_no_coinciden_no_se_vuelve_a_sellar()
    {
        var caso = await CasoConEvidenciasAsync(1);
        await servidor.SellarPendientesAsync();
        await servidor.ReconstruirCadenaAsync(caso);
        (await _ana.IngestarAsync(caso, RandomNumberGenerator.GetBytes(64))).EnsureSuccessStatusCode();

        await servidor.SellarPendientesAsync();

        // Sellar la cadena reconstruida sería darle un testigo a la falsificación.
        Assert.Equal(2, Assert.Single(await SellosDeAsync(caso)).Secuencia);
    }

    [Fact]
    public async Task El_sellado_periodico_sella_las_cadenas_en_segundo_plano()
    {
        var caso = await CasoConEvidenciasAsync(1);

        using (var conSellador = servidor.WithWebHostBuilder(b => b.UseSetting("Custodia:Sellado:Intervalo", "00:00:00.200")))
        {
            conSellador.CreateClient();
            var limite = DateTime.UtcNow.AddSeconds(30);
            while ((await SellosDeAsync(caso)).Count == 0 && DateTime.UtcNow < limite)
                await Task.Delay(100);
        }

        Assert.Equal(2, Assert.Single(await SellosDeAsync(caso)).Secuencia);
    }

    [Fact]
    public void Sin_clave_de_sellado_el_servidor_no_arranca()
    {
        using var sinClave = servidor.WithWebHostBuilder(b => b.UseSetting("Custodia:Sellado:RutaClavePrivada", ""));

        var ex = Assert.ThrowsAny<Exception>(() => sinClave.CreateClient());
        Assert.Contains("clave de sellado", ex.ToString());
    }
}
