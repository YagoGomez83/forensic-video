using System.Security.Cryptography;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Operaciones;
using Forensic.Domain.Comun;

namespace Forensic.IntegrationTests;

/// <summary>
/// Cada formulario de operación del cliente arma sus parámetros en las unidades del servidor. Acá se
/// mandan al servidor real: si un nombre o una unidad no coincide con su catálogo, responde 422 y el
/// test falla.
/// </summary>
public class ContratoDeLosFormulariosDelClienteTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private static readonly PuntoDeImagen[] Esquinas = [new(3, 2), new(60, 4), new(62, 45), new(1, 44)];

    public static TheoryData<string, string> Formularios => new()
    {
        { nameof(FormularioDeRecorte), """{"alto":9,"ancho":16,"x":5,"y":6}""" },
        { nameof(FormularioDePerspectiva), """{"alto":30,"ancho":40,"interpolacion":"cubica","x1":3,"x2":60,"x3":62,"x4":1,"y1":2,"y2":4,"y3":45,"y4":44}""" },
        { nameof(FormularioDeRotacion90), """{"giros":2}""" },
        { nameof(FormularioDeRotacion), """{"centesimasDeGrado":-1250,"interpolacion":"lineal"}""" },
        { nameof(FormularioDeDenoiseNlMeans), """{"busqueda":21,"h":10,"hColor":12,"plantilla":7}""" },
        { nameof(FormularioDeDenoiseMediana), """{"ventana":5}""" },
        { nameof(FormularioDeUnsharp), """{"cantidadCentesimas":133,"sigmaCentesimas":237}""" },
        { nameof(FormularioDeClahe), """{"cuadricula":8,"limiteDecimas":25}""" },
        { nameof(FormularioDeBrilloContraste), """{"brillo":-21,"contrasteCentesimas":137}""" },
        { nameof(FormularioDeGamma), """{"gammaMilesimas":2200}""" },
        { nameof(FormularioDeEcualizacion), """{}""" },
    };

    private static (FormularioDeOperacion Formulario, MarcasDelVisor Marcas) Armar(string nombre) => nombre switch
    {
        nameof(FormularioDeRecorte) => (new FormularioDeRecorte(), new(new(5, 6, 16, 9), [])),
        nameof(FormularioDePerspectiva) => (new FormularioDePerspectiva { Ancho = 40, Alto = 30, Interpolacion = "cubica" }, new(null, Esquinas)),
        nameof(FormularioDeRotacion90) => (new FormularioDeRotacion90 { Giros = 2 }, new(null, [])),
        nameof(FormularioDeRotacion) => (new FormularioDeRotacion { Grados = -12.5m }, new(null, [])),
        nameof(FormularioDeDenoiseNlMeans) => (new FormularioDeDenoiseNlMeans { HColor = 12 }, new(null, [])),
        nameof(FormularioDeDenoiseMediana) => (new FormularioDeDenoiseMediana(), new(null, [])),
        nameof(FormularioDeUnsharp) => (new FormularioDeUnsharp { Sigma = 2.37m, Cantidad = 1.33m }, new(null, [])),
        nameof(FormularioDeClahe) => (new FormularioDeClahe { Limite = 2.5m }, new(null, [])),
        nameof(FormularioDeBrilloContraste) => (new FormularioDeBrilloContraste { Contraste = 1.37m, Brillo = -21 }, new(null, [])),
        nameof(FormularioDeGamma) => (new FormularioDeGamma { Gamma = 2.2m }, new(null, [])),
        nameof(FormularioDeEcualizacion) => (new FormularioDeEcualizacion(), new(null, [])),
        _ => throw new ArgumentOutOfRangeException(nameof(nombre), nombre, null),
    };

    [Theory]
    [MemberData(nameof(Formularios))]
    public async Task El_servidor_acepta_los_parametros_de_cada_formulario_y_los_asienta_canonicos(string nombre, string canonicos)
    {
        var usuario = $"frm-{Guid.NewGuid():N}"[..16];
        await servidor.UsuarioConSesionAsync(usuario, Rol.Analista, "la frase de la analista");
        servidor.CreateClient().Dispose();
        var cliente = ClienteForense.Crear(servidor.Server.BaseAddress, new SesionEnMemoria(), servidor.Server.CreateHandler());
        await cliente.IniciarSesionAsync(usuario, "la frase de la analista", CancellationToken.None);
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "formularios", CancellationToken.None);
        var bytes = RandomNumberGenerator.GetBytes(4096);
        var original = await cliente.IngestarAsync(caso.Id, "cam1.png", new MemoryStream(bytes), bytes.Length, null, CancellationToken.None);
        var (formulario, marcas) = Armar(nombre);

        var aplicada = await cliente.AplicarOperacionAsync(caso.Id, original.Id, formulario.Tipo, formulario.Parametros(marcas), null, CancellationToken.None);

        Assert.Equal(formulario.Tipo, aplicada.Tipo);
        Assert.Equal(canonicos, aplicada.Parametros);
    }
}
