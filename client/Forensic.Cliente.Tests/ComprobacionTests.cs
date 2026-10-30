using System.Net;
using Forensic.Cliente.Api;
using Forensic.Cliente.Tests.Dobles;

namespace Forensic.Cliente.Tests;

/// <summary>
/// <c>Forensic.Cliente.exe --comprobar</c>: lo corre el admin al instalar (y el CI sobre el ejecutable
/// publicado) para saber si la workstation llega al servidor, sin abrir la ventana.
/// </summary>
public sealed class ComprobacionTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("forensic-comprobar-").FullName;
    private readonly StringWriter _salida = new();

    private string Archivo => Path.Combine(_carpeta, "cliente.json");

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private Task<int> Comprobar(string json, HttpMessageHandler? canal)
    {
        File.WriteAllText(Archivo, json);
        return Comprobacion.EjecutarAsync(Archivo, _ => null, _salida, _ => canal, CancellationToken.None);
    }

    [Fact]
    public async Task Si_el_servidor_responde_sale_con_cero()
    {
        // Sin sesión el servidor contesta 401: igual respondió, así que el canal (y el TLS mutuo) funciona.
        var servidor = new ServidorFalso().En(HttpMethod.Get, "/casos", HttpStatusCode.Unauthorized);

        var codigo = await Comprobar("""{ "servidor": "http://localhost:5065/" }""", servidor);

        Assert.Equal(Comprobacion.Correcta, codigo);
        Assert.Single(servidor.Peticiones);
        Assert.Contains(Archivo, _salida.ToString());
        Assert.Contains("http://localhost:5065/", _salida.ToString());
        Assert.Contains("http://127.0.0.1:8100/", _salida.ToString());
    }

    [Fact]
    public async Task Una_configuracion_invalida_sale_con_uno_y_dice_por_que()
    {
        var codigo = await Comprobar("""{ "servidor": "http://custodia.lab/" }""", new ServidorFalso());

        Assert.Equal(Comprobacion.ConfiguracionInvalida, codigo);
        Assert.Contains("https", _salida.ToString());
    }

    [Fact]
    public async Task Un_certificado_que_no_carga_sale_con_uno()
    {
        File.WriteAllText(Archivo, """{ "servidor": "http://localhost:5065/" }""");

        var codigo = await Comprobacion.EjecutarAsync(Archivo, _ => null, _salida,
            _ => throw new InvalidOperationException("No está en el almacén personal un certificado con clave y huella 0123."),
            CancellationToken.None);

        Assert.Equal(Comprobacion.ConfiguracionInvalida, codigo);
        Assert.Contains("huella 0123", _salida.ToString());
    }

    [Fact]
    public async Task Si_el_servidor_no_responde_sale_con_dos()
    {
        var codigo = await Comprobar("""{ "servidor": "http://localhost:5065/" }""", new ServidorFalso { Inalcanzable = true });

        Assert.Equal(Comprobacion.ServidorInalcanzable, codigo);
        Assert.Contains("No se puede establecer una conexión.", _salida.ToString());
    }
}
