using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Casos;

/// <summary>Un archivo elegido por el analista para ingestar. Se lee directo, sin copias temporales.</summary>
public sealed record ArchivoParaIngestar(string Nombre, long TamanoBytes, Func<Task<Stream>> Abrir);

/// <summary>
/// Un caso: sus evidencias y su cadena de custodia. La integridad de la cadena es el veredicto del
/// servidor; el cliente no recalcula ningún hash.
/// </summary>
public sealed partial class CasoViewModel(ClienteForense api, INavegacion navegacion, CasoDto caso) : ObservableObject
{
    public CasoDto Caso { get; } = caso;

    public ObservableCollection<EvidenciaDto> Evidencias { get; } = [];
    public ObservableCollection<AsientoDto> Asientos { get; } = [];

    [ObservableProperty]
    private bool _cadenaIntegra;

    [ObservableProperty]
    private string? _estadoDeLaCadena;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _aviso;

    [ObservableProperty]
    private bool _ingestando;

    /// <summary>De 0 a 1: la parte del archivo que ya salió hacia el servidor.</summary>
    [ObservableProperty]
    private double _progreso;

    internal ClienteForense Api => api;
    internal INavegacion Navegacion => navegacion;

    [RelayCommand]
    private async Task CargarAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            var evidencias = await api.ListarEvidenciasAsync(Caso.Id, ct);
            var custodia = await api.ExportarCustodiaAsync(Caso.Id, ct);
            Reemplazar(Evidencias, evidencias);
            Reemplazar(Asientos, custodia.Asientos);
            var v = custodia.Verificacion;
            CadenaIntegra = v.EsIntegra;
            EstadoDeLaCadena = v.EsIntegra
                ? $"Cadena íntegra: {v.CantidadAsientos} asientos verificados por el servidor."
                : $"CADENA COMPROMETIDA en el asiento {v.SecuenciaRota}: {v.Motivo}";
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

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task IngestarAsync(ArchivoParaIngestar archivo, CancellationToken ct)
    {
        (Error, Aviso, Progreso, Ingestando) = (null, null, 0, true);
        var enUi = EnElHiloActual();
        try
        {
            await using var contenido = await archivo.Abrir();
            await api.IngestarAsync(
                Caso.Id, archivo.Nombre, contenido, archivo.TamanoBytes,
                enviados => enUi(() => Progreso = archivo.TamanoBytes == 0 ? 1 : (double)enviados / archivo.TamanoBytes), ct);
            await CargarAsync(ct);
            Aviso = $"Evidencia «{archivo.Nombre}» ingestada y custodiada por el servidor.";
        }
        catch (OperationCanceledException)
        {
            Aviso = "Ingesta cancelada: el servidor no recibió el archivo completo.";
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
            Ingestando = false;
        }
    }

    [RelayCommand]
    private void AbrirEvidencia(EvidenciaDto evidencia) =>
        navegacion.IrA(new Visor.VisorViewModel(api, navegacion, this, evidencia));

    [RelayCommand]
    private void VerInformes() => navegacion.IrA(new Informes.InformesViewModel(api, navegacion, this));

    [RelayCommand]
    private void Volver() => navegacion.IrACasos();

    /// <summary>El progreso llega desde el hilo que sube; se publica en el de la UI si lo hay.</summary>
    private static Action<Action> EnElHiloActual()
    {
        var contexto = SynchronizationContext.Current;
        return contexto is null ? accion => accion() : accion => contexto.Post(_ => accion(), null);
    }

    private static void Reemplazar<T>(ObservableCollection<T> destino, IEnumerable<T> origen)
    {
        destino.Clear();
        foreach (var item in origen)
            destino.Add(item);
    }
}
