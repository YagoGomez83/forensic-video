using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.Api;

/// <summary>Los informes por la API: cada tipo por su propia ruta, y la descarga sin dejar archivos vacíos.</summary>
public class InformesApiTests
{
    private readonly ServidorFalso _servidor = new();
    private readonly ClienteForense _api;

    public InformesApiTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    /// <summary>Un informe como lo devuelve el servidor: el tipo va en minúsculas («pericial», «investigativo»).</summary>
    internal static object Informe(string tipo = "pericial", long secuencia = 7, int excluidas = 0, int opacos = 0) => new
    {
        tipo, secuencia, emitidoEn = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero), emitidoPor = "sergio", hasta = secuencia - 1,
        pdf = new { sha256 = new string('p', 64), tamanoBytes = 62_056 },
        bundle = new { sha256 = new string('b', 64), tamanoBytes = 62_951 },
        evidencias = 2, evidenciasExcluidas = excluidas, asientosOpacos = opacos,
    };

    [Fact]
    public async Task Lista_los_informes_del_caso_con_su_tipo()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/informes", HttpStatusCode.OK,
            new[] { Informe("pericial", 7), Informe("investigativo", 9, excluidas: 0, opacos: 0) });

        var informes = await _api.ListarInformesAsync(CasoId, CancellationToken.None);

        Assert.Equal([(TipoDeInforme.Pericial, 7L), (TipoDeInforme.Investigativo, 9L)], informes.Select(i => (i.Tipo, i.Secuencia)));
        Assert.Equal((new string('p', 64), 62_056L), (informes[0].Pdf.Sha256, informes[0].Pdf.TamanoBytes));
        Assert.Equal(6, informes[0].Hasta);
    }

    [Theory]
    [InlineData(TipoDeInforme.Pericial, "pericial")]
    [InlineData(TipoDeInforme.Investigativo, "investigativo")]
    public async Task Cada_tipo_se_emite_por_su_propia_ruta(TipoDeInforme tipo, string ruta)
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/informes/{ruta}", HttpStatusCode.Created, Informe(ruta));

        var informe = await _api.EmitirInformeAsync(CasoId, tipo, CancellationToken.None);

        Assert.Equal(tipo, informe.Tipo);
        Assert.Equal($"/casos/{CasoId}/informes/{ruta}", Assert.Single(_servidor.Peticiones).RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(TipoDeInforme.Pericial, ArchivoDeInforme.Pdf, "pericial/7/pdf")]
    [InlineData(TipoDeInforme.Pericial, ArchivoDeInforme.Bundle, "pericial/7/bundle")]
    [InlineData(TipoDeInforme.Investigativo, ArchivoDeInforme.Pdf, "investigativo/7/pdf")]
    [InlineData(TipoDeInforme.Investigativo, ArchivoDeInforme.Bundle, "investigativo/7/bundle")]
    public async Task Descarga_cada_archivo_por_la_ruta_de_su_tipo(TipoDeInforme tipo, ArchivoDeInforme archivo, string ruta)
    {
        var bytes = Enumerable.Range(0, 70_000).Select(i => (byte)(i * 7)).ToArray();
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/informes/{ruta}",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var destino = new DestinoEnMemoria();

        await _api.DescargarInformeAAsync(CasoId, tipo, 7, archivo, () => Task.FromResult<Stream>(destino), CancellationToken.None);

        Assert.Equal(bytes, destino.ToArray());
    }

    [Fact]
    public async Task Si_el_servidor_rechaza_la_descarga_no_se_abre_el_destino()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/informes/pericial/7/bundle",
            _ => ServidorFalso.Problema(HttpStatusCode.Locked, "Integridad comprometida", "El bundle no coincide con el hash asentado."));
        var abierto = false;

        var ex = await Assert.ThrowsAsync<ErrorDeApi>(() => _api.DescargarInformeAAsync(CasoId, TipoDeInforme.Pericial, 7, ArchivoDeInforme.Bundle, () =>
        {
            abierto = true;
            return Task.FromResult<Stream>(new MemoryStream());
        }, CancellationToken.None));

        Assert.Equal(TipoDeError.IntegridadComprometida, ex.Tipo);
        Assert.False(abierto);
    }
}
