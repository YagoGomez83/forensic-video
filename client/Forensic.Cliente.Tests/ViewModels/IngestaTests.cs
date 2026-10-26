using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Casos;
using Forensic.Cliente.Tests.Dobles;
using static Forensic.Cliente.Tests.ViewModels.CasosViewModelsTests;

namespace Forensic.Cliente.Tests.ViewModels;

public class IngestaTests
{
    private readonly ServidorFalso _servidor = new();
    private readonly ClienteForense _api;

    public IngestaTests()
    {
        _api = ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), _servidor);
        _api.Sesion.Iniciar("t", DateTimeOffset.UtcNow.AddMinutes(15));
        _servidor.En(HttpMethod.Get, $"/casos/{CasoId}/evidencias", HttpStatusCode.OK, new[] { Evidencia() })
            .En(HttpMethod.Get, $"/casos/{CasoId}/custodia", HttpStatusCode.OK, Export());
    }

    private async Task<CasoViewModel> CasoAsync()
    {
        var servidor = new ServidorFalso().En(HttpMethod.Get, "/casos", HttpStatusCode.OK, new[] { Caso() });
        var caso = (await ClienteForense.Crear(new Uri("http://servidor/"), new SesionEnMemoria(), servidor).ListarCasosAsync(CancellationToken.None))[0];
        return new CasoViewModel(_api, new NavegacionRegistrada(), caso);
    }

    private static ArchivoParaIngestar Archivo(byte[] bytes, string nombre = "cámara 1.mp4") =>
        new(nombre, bytes.Length, () => Task.FromResult<Stream>(new MemoryStream(bytes)));

    [Fact]
    public async Task Sube_los_bytes_exactos_del_archivo_informa_el_progreso_y_recarga_el_caso()
    {
        // xUnit corre con su propio SynchronizationContext y el ViewModel publicaría el progreso con Post
        // (lo correcto en la UI): sin contexto, se publica en el acto y el test ve cada valor.
        SynchronizationContext.SetSynchronizationContext(null);
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias*", HttpStatusCode.Created, Evidencia(nombre: "cámara 1.mp4"));
        var vm = await CasoAsync();
        var progresos = new List<double>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CasoViewModel.Progreso)) progresos.Add(vm.Progreso); };

        await vm.IngestarCommand.ExecuteAsync(Archivo(bytes));

        var subida = _servidor.Peticiones.Single(p => p.Method == HttpMethod.Post);
        Assert.Equal(bytes, _servidor.Bytes[_servidor.Peticiones.IndexOf(subida)]);
        Assert.Equal("application/octet-stream", subida.Content!.Headers.ContentType!.MediaType);
        Assert.Equal($"/casos/{CasoId}/evidencias?nombre=c%C3%A1mara%201.mp4", subida.RequestUri!.PathAndQuery);
        Assert.True(progresos.Count > 2, string.Join(", ", progresos));
        Assert.Equal(progresos.OrderBy(p => p), progresos);
        Assert.Equal(1.0, progresos[^1]);
        Assert.False(vm.Ingestando);
        Assert.Equal("Evidencia «cámara 1.mp4» ingestada y custodiada por el servidor.", vm.Aviso);
        Assert.Single(vm.Evidencias);
    }

    [Fact]
    public async Task La_ingesta_se_puede_cancelar_a_mitad_de_camino()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias*", HttpStatusCode.Created, Evidencia());
        var vm = await CasoAsync();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CasoViewModel.Progreso) && vm.Progreso > 0)
                vm.IngestarCommand.Cancel();
        };

        await vm.IngestarCommand.ExecuteAsync(Archivo(new byte[2_000_000]));

        Assert.False(vm.Ingestando);
        Assert.Equal("Ingesta cancelada: el servidor no recibió el archivo completo.", vm.Aviso);
        Assert.DoesNotContain(_servidor.Peticiones, p => p.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Si_el_servidor_rechaza_la_evidencia_muestra_su_motivo()
    {
        _servidor.En(HttpMethod.Post, $"/casos/{CasoId}/evidencias*", _ => ServidorFalso.Problema(HttpStatusCode.UnprocessableEntity, "Regla", "Una evidencia vacía no es admisible."));
        var vm = await CasoAsync();

        await vm.IngestarCommand.ExecuteAsync(Archivo([]));

        Assert.Equal("Una evidencia vacía no es admisible.", vm.Error);
        Assert.False(vm.Ingestando);
    }
}
