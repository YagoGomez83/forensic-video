using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo;
using Forensic.Cliente.Nucleo.Visor;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.ViewModels;

public class DescargaYMensajesTests
{
    private readonly ServidorFalso _servidor = new();
    private readonly ClienteForense _api;

    public DescargaYMensajesTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task Descargar_escribe_los_bytes_que_entrega_el_servidor_en_el_destino_elegido()
    {
        var bytes = Enumerable.Range(0, 150_000).Select(i => (byte)i).ToArray();
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{EvidenciaId}/contenido",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var vm = new VisorViewModel(_api, new NavegacionRegistrada(), null, await VisorViewModelTests.EvidenciaDtoAsync());
        var destino = new MemoriaQueNoSeCierra();

        await vm.DescargarCommand.ExecuteAsync(new DestinoDeDescarga("cam1.png", () => Task.FromResult<Stream>(destino)));

        Assert.Equal(bytes, destino.ToArray());
        Assert.Equal($"Descargada en «cam1.png». El servidor verificó su SHA-256 {new string('a', 64)} antes de entregarla.", vm.Aviso);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Si_el_servidor_bloquea_la_descarga_por_integridad_lo_muestra()
    {
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias/{EvidenciaId}/contenido",
            _ => ServidorFalso.Problema(HttpStatusCode.Locked, "Integridad comprometida", "El blob no coincide con su hash."));
        var vm = new VisorViewModel(_api, new NavegacionRegistrada(), null, await VisorViewModelTests.EvidenciaDtoAsync());

        var abierto = false;

        await vm.DescargarCommand.ExecuteAsync(new DestinoDeDescarga("x.png", () =>
        {
            abierto = true;
            return Task.FromResult<Stream>(new MemoryStream());
        }));

        Assert.Equal("El blob no coincide con su hash.", vm.Error);
        Assert.Null(vm.Aviso);
        // El archivo elegido no se crea si el servidor no entrega la evidencia.
        Assert.False(abierto);
    }

    [Theory]
    [InlineData(TipoDeError.ServidorInalcanzable, "No se pudo conectar", "No se pudo conectar")]
    [InlineData(TipoDeError.DemasiadosIntentos, "Too Many Requests", "Demasiados intentos")]
    [InlineData(TipoDeError.WorkerNoDisponible, "caído", "servicio de procesamiento no está disponible")]
    [InlineData(TipoDeError.DivergenciaDeSeguridad, "otro hash", "ALERTA DE SEGURIDAD")]
    [InlineData(TipoDeError.Rechazado, "La región se sale de la imagen.", "La región se sale de la imagen.")]
    public void Cada_error_tiene_un_mensaje_claro_para_el_analista(TipoDeError tipo, string detalle, string esperado)
    {
        Assert.Contains(esperado, Mensajes.Para(new ErrorDeApi(tipo, null, detalle)));
    }

    /// <summary>El destino lo cierra el ViewModel al terminar; el test lo lee después.</summary>
    private sealed class MemoriaQueNoSeCierra : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
        }
    }
}
