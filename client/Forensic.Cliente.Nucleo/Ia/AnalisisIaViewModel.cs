using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Operaciones;
using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Nucleo.Ia;

/// <summary>
/// Un hallazgo tal como se muestra: lo que dijo el modelo, nunca como un hecho. La región va en
/// píxeles de la imagen, igual que la reportó el worker.
/// </summary>
public sealed record HallazgoALaVista(Region Region, string Etiqueta, string Descripcion)
{
    // Coma decimal fija: el cliente corre con globalización invariante y no depende de la cultura del sistema.
    private static readonly NumberFormatInfo Cultura = new() { NumberDecimalSeparator = "," };

    public static HallazgoALaVista Desde(HallazgoDto h)
    {
        // Sólo para mostrar: la confianza exacta queda asentada en el servidor.
        var confianza = h.Confianza.ToString("0.00", Cultura);
        var region = new Region(h.Region.X, h.Region.Y, h.Region.Ancho, h.Region.Alto);
        return h.TextoLeido is { } texto
            ? new(region, $"«{texto}» {confianza}", $"el modelo leyó «{texto}» con {confianza}")
            : new(region, $"{h.Clase} {confianza}", $"el modelo detectó «{h.Clase}» con {confianza}");
    }
}

/// <summary>
/// La pantalla de IA de una evidencia, aparte del visor: lo que produce es «sólo investigación» y no
/// se mezcla con las operaciones admisibles. El worker corre en esta workstation sobre el contenido que
/// el servidor ya entregó verificado; el servidor cruza el hash, valida el modelo y lo asienta.
/// </summary>
public sealed partial class AnalisisIaViewModel(
    ClienteForense api, OrquestadorDeAnalisisIa orquestador, INavegacion navegacion, VisorViewModel visor) : ObservableObject
{
    /// <summary>El visor de donde se vino: su imagen en memoria, su zoom y su desplazamiento.</summary>
    public VisorViewModel Visor { get; } = visor;

    public EvidenciaDto Evidencia => Visor.Evidencia;

    [ObservableProperty]
    private IReadOnlyList<AnalisisIaDto> _analisis = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hallazgos))]
    private AnalisisIaDto? _analisisElegido;

    /// <summary>Los hallazgos del análisis elegido; se dibujan sobre la imagen.</summary>
    public IReadOnlyList<HallazgoALaVista> Hallazgos =>
        AnalisisElegido?.Hallazgos.Select(HallazgoALaVista.Desde).ToList() ?? [];

    /// <summary>Umbral de confianza: se envía en milésimas, sin redondear.</summary>
    [ObservableProperty]
    private decimal? _umbral = 0.5m;

    [ObservableProperty]
    private bool _ocupado;

    [ObservableProperty]
    private string? _error;

    [RelayCommand]
    private async Task CargarAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            var analisis = await api.ListarAnalisisIaAsync(Evidencia.CasoId, Evidencia.Id, ct);
            Analisis = analisis.OrderBy(a => a.RegistradoEn).ToList();
            AnalisisElegido = Analisis.Count > 0 ? Analisis[^1] : null;
        }
        catch (SesionExpiradaException)
        {
            // La vuelta al login la hace VentanaViewModel con el evento de la sesión.
        }
        catch (ErrorDeApi ex)
        {
            Error = Mensajes.Para(ex);
        }
    }

    [RelayCommand]
    private Task DetectarAsync(CancellationToken ct) =>
        AnalizarAsync((contenido, umbral) => orquestador.DetectarAsync(Evidencia.CasoId, Evidencia.Id, contenido, umbral, ct));

    [RelayCommand]
    private Task LeerTextoAsync(CancellationToken ct) =>
        AnalizarAsync((contenido, umbral) => orquestador.LeerTextoAsync(Evidencia.CasoId, Evidencia.Id, contenido, umbral, ct));

    /// <summary>El derivado lo crea el servidor («sólo investigación») y se abre en el visor.</summary>
    [RelayCommand]
    private Task SuperResolverAsync(CancellationToken ct) =>
        EjecutarAsync(async contenido =>
        {
            var aplicada = await orquestador.SuperResolverAsync(Evidencia.CasoId, Evidencia.Id, contenido, null, ct);
            navegacion.IrA(new VisorViewModel(api, navegacion, Visor.VolverA, aplicada.Derivado, $"{aplicada.Tipo} {aplicada.Parametros}"));
        });

    [RelayCommand]
    private void Volver() => navegacion.IrA(Visor);

    private async Task AnalizarAsync(Func<byte[], int, Task<AnalisisIaDto>> analizar)
    {
        Error = null;
        int umbral;
        try
        {
            umbral = PuntoFijo.AEntero(Umbral, 3, "el umbral");
        }
        catch (ErrorDeFormulario ex)
        {
            Error = ex.Message;
            return;
        }
        await EjecutarAsync(async contenido =>
        {
            var registrado = await analizar(contenido, umbral);
            Analisis = [.. Analisis, registrado];
            AnalisisElegido = registrado;
        });
    }

    private async Task EjecutarAsync(Func<byte[], Task> accion)
    {
        Error = null;
        if (Visor.Contenido is not { } contenido)
        {
            Error = "La evidencia no está cargada en memoria: sin su contenido no hay análisis de IA.";
            return;
        }
        Ocupado = true;
        try
        {
            await accion(contenido);
        }
        catch (SesionExpiradaException)
        {
            // La vuelta al login la hace VentanaViewModel con el evento de la sesión.
        }
        catch (ErrorDeApi ex)
        {
            Error = Mensajes.Para(ex);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
