using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forensic.Cliente.Api;
using Forensic.Cliente.Nucleo.Casos;
using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Nucleo.Informes;

/// <summary>Qué archivo de qué informe bajar, y adónde.</summary>
public sealed record DescargaDeInforme(InformeEmitidoDto Informe, ArchivoDeInforme Archivo, DestinoDeDescarga Destino);

/// <summary>Los informes del caso: emitirlos (con confirmación) y bajar su PDF o su bundle.</summary>
public sealed partial class InformesViewModel(ClienteForense api, INavegacion navegacion, CasoViewModel caso) : ObservableObject
{
    public CasoDto Caso => caso.Caso;


    public ObservableCollection<InformeEmitidoDto> Periciales { get; } = [];
    public ObservableCollection<InformeEmitidoDto> Investigativos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmandoPericial), nameof(ConfirmandoInvestigativo))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarEmisionCommand))]
    private TipoDeInforme? _emisionPendiente;

    [ObservableProperty]
    private bool _emitiendo;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _aviso;

    public bool ConfirmandoPericial => EmisionPendiente == TipoDeInforme.Pericial;
    public bool ConfirmandoInvestigativo => EmisionPendiente == TipoDeInforme.Investigativo;

    [RelayCommand]
    private async Task CargarAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            var informes = await api.ListarInformesAsync(Caso.Id, ct);
            Reemplazar(Periciales, informes.Where(i => i.Tipo == TipoDeInforme.Pericial));
            Reemplazar(Investigativos, informes.Where(i => i.Tipo == TipoDeInforme.Investigativo));
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

    /// <summary>Primer paso: emitir es irreversible, así que antes se pide confirmar. No llama al servidor.</summary>
    [RelayCommand]
    private void PrepararEmision(TipoDeInforme tipo) => (EmisionPendiente, Error, Aviso) = (tipo, null, null);

    [RelayCommand]
    private void CancelarEmision() => EmisionPendiente = null;

    [RelayCommand(CanExecute = nameof(HayEmisionPendiente))]
    private async Task ConfirmarEmisionAsync(CancellationToken ct)
    {
        if (EmisionPendiente is not { } tipo)
            return;
        (Error, Aviso, Emitiendo) = (null, null, true);
        try
        {
            var informe = await api.EmitirInformeAsync(Caso.Id, tipo, ct);
            await CargarAsync(ct);
            Aviso = Emitido(informe);
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
            // Lo que pasó queda a la vista; otra emisión se vuelve a confirmar.
            (EmisionPendiente, Emitiendo) = (null, false);
        }
    }

    private bool HayEmisionPendiente() => EmisionPendiente is not null;

    [RelayCommand]
    private async Task DescargarAsync(DescargaDeInforme descarga, CancellationToken ct)
    {
        (Error, Aviso) = (null, null);
        var (informe, archivo, destino) = descarga;
        try
        {
            await api.DescargarInformeAAsync(Caso.Id, informe.Tipo, informe.Secuencia, archivo, destino.Abrir, ct);
            var hash = archivo == ArchivoDeInforme.Pdf ? informe.Pdf.Sha256 : informe.Bundle.Sha256;
            Aviso = $"Descargado en «{destino.Nombre}». El servidor verificó su SHA-256 {hash} antes de entregarlo.";
        }
        catch (OperationCanceledException)
        {
            Aviso = "Descarga cancelada.";
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
    private void Volver() => navegacion.IrA(caso);

    /// <summary>Un nombre para el diálogo de guardado: expediente, tipo y asiento de emisión.</summary>
    public string NombreSugerido(InformeEmitidoDto informe, ArchivoDeInforme archivo)
    {
        var tipo = informe.Tipo == TipoDeInforme.Pericial ? "pericial" : "investigativo";
        var nombre = $"{Caso.NumeroExpediente}-informe-{tipo}-{informe.Secuencia}";
        return archivo == ArchivoDeInforme.Pdf ? nombre + ".pdf" : nombre + "-bundle.zip";
    }

    /// <summary>Lo que asentó el servidor, dicho tal cual: el cliente no cuenta ni decide qué entró.</summary>
    private static string Emitido(InformeEmitidoDto i)
    {
        var cual = i.Tipo == TipoDeInforme.Pericial ? "Informe pericial" : "Informe investigativo (SÓLO INVESTIGACIÓN, no es prueba)";
        var texto = $"{cual} emitido y asentado en el asiento {i.Secuencia}: cubre la cadena hasta el asiento {i.Hasta}, " +
            $"con {Cantidad(i.Evidencias, "evidencia", "evidencias")}";
        if (i.Tipo == TipoDeInforme.Pericial)
            texto += $"; {Cantidad(i.EvidenciasExcluidas, "evidencia excluida", "evidencias excluidas")} por ser «sólo investigación» " +
                $"y {Cantidad(i.AsientosOpacos, "asiento opaco", "asientos opacos")}";
        return texto + ".";
    }

    private static string Cantidad(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

    private static void Reemplazar<T>(ObservableCollection<T> destino, IEnumerable<T> origen)
    {
        destino.Clear();
        foreach (var item in origen)
            destino.Add(item);
    }
}
