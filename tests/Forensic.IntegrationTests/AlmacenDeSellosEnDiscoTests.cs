using Forensic.Application;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Infrastructure.Sellado;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Forensic.IntegrationTests;

public sealed class AlmacenDeSellosEnDiscoTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "forensic-video-tests", $"sellos-{Guid.NewGuid():N}");
    private readonly FirmanteEd25519 _firmante = FirmanteEd25519.DesdePem(ClaveDePrueba.Nueva());
    private readonly AlmacenDeSellosEnDisco _almacen;
    private readonly Guid _caso = Guid.NewGuid();

    public AlmacenDeSellosEnDiscoTests() =>
        _almacen = new AlmacenDeSellosEnDisco(
            Options.Create(new OpcionesSellado { RaizDeSellos = _raiz }), NullLogger<AlmacenDeSellosEnDisco>.Instance);

    public void Dispose()
    {
        if (!Directory.Exists(_raiz))
            return;
        foreach (var f in Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_raiz, recursive: true);
    }

    private SelloDeCadena SelloDeLaSecuencia(int secuencia, DateTimeOffset? selladoEn = null)
    {
        AsientoDeCustodia? ultimo = null;
        for (var i = 0; i < secuencia; i++)
            ultimo = RegistroDeCustodia.Asentar(ultimo, _caso, TipoAsiento.CasoAbierto, null, "{}", new Actor("ana", Rol.Analista), Ahora);
        return SelloDeCadena.Sellar(ultimo!, selladoEn ?? Ahora, _firmante.ClaveId, _firmante.Firmar);
    }

    [Fact]
    public async Task Un_sello_guardado_se_lee_identico_y_sigue_verificando()
    {
        var sello = SelloDeLaSecuencia(3, Ahora.AddTicks(1234560));

        Assert.True(await _almacen.AgregarAsync(sello, CancellationToken.None));
        var leido = Assert.Single(await _almacen.ListarAsync(_caso, CancellationToken.None));

        Assert.Equal(sello.CasoId, leido.CasoId);
        Assert.Equal(sello.Secuencia, leido.Secuencia);
        Assert.Equal(sello.HashCabeza, leido.HashCabeza);
        Assert.Equal(sello.SelladoEn, leido.SelladoEn);
        Assert.Equal(sello.ClaveId, leido.ClaveId);
        Assert.Equal(sello.Firma, leido.Firma);
        Assert.True(_firmante.Verificar(leido));
    }

    [Fact]
    public async Task Una_secuencia_ya_sellada_no_se_sobreescribe()
    {
        var primero = SelloDeLaSecuencia(2);
        var segundo = SelloDeLaSecuencia(2, Ahora.AddMinutes(5));

        Assert.True(await _almacen.AgregarAsync(primero, CancellationToken.None));
        Assert.False(await _almacen.AgregarAsync(segundo, CancellationToken.None));

        Assert.Equal(primero.SelladoEn, Assert.Single(await _almacen.ListarAsync(_caso, CancellationToken.None)).SelladoEn);
    }

    [Fact]
    public async Task El_archivo_del_sello_queda_de_solo_lectura()
    {
        await _almacen.AgregarAsync(SelloDeLaSecuencia(1), CancellationToken.None);

        var archivo = Assert.Single(Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories));
        Assert.True(File.GetAttributes(archivo).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task Lista_los_sellos_del_caso_ordenados_por_secuencia_y_sin_los_de_otros_casos()
    {
        foreach (var s in new[] { 10, 2, 7 })
            await _almacen.AgregarAsync(SelloDeLaSecuencia(s), CancellationToken.None);
        var otro = new AlmacenDeSellosEnDiscoTests();
        try
        {
            await _almacen.AgregarAsync(otro.SelloDeLaSecuencia(1), CancellationToken.None);
        }
        finally
        {
            otro.Dispose();
        }

        var sellos = await _almacen.ListarAsync(_caso, CancellationToken.None);

        Assert.Equal(new long[] { 2, 7, 10 }, sellos.Select(s => s.Secuencia));
        Assert.Empty(await _almacen.ListarAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Un_sello_editado_en_disco_se_lee_pero_su_firma_lo_delata()
    {
        var sello = SelloDeLaSecuencia(1);
        await _almacen.AgregarAsync(sello, CancellationToken.None);
        var archivo = Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories).Single();
        File.SetAttributes(archivo, FileAttributes.Normal);
        File.WriteAllText(archivo, File.ReadAllText(archivo).Replace(sello.HashCabeza.Hex, new string('a', 64)));

        var leido = Assert.Single(await _almacen.ListarAsync(_caso, CancellationToken.None));

        Assert.False(_firmante.Verificar(leido));
    }

    [Fact]
    public async Task Un_sello_ilegible_compromete_la_cadena()
    {
        await _almacen.AgregarAsync(SelloDeLaSecuencia(1), CancellationToken.None);
        var archivo = Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories).Single();
        File.SetAttributes(archivo, FileAttributes.Normal);
        File.WriteAllText(archivo, "{ no es json");

        await Assert.ThrowsAsync<CadenaComprometidaException>(() => _almacen.ListarAsync(_caso, CancellationToken.None));
    }
}
