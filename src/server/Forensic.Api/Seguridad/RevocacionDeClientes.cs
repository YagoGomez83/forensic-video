using System.Security.Cryptography.X509Certificates;

namespace Forensic.Api.Seguridad;

/// <summary>
/// La revocación de certificados de cliente con la CRL del laboratorio en disco. La relee en cada
/// conexión y la vuelve a validar si cambió su contenido: revocar una workstation es copiar la CRL nueva,
/// sin reiniciar. Falla cerrada: con la CRL vencida, ilegible o de otra CA no entra nadie.
/// </summary>
internal sealed class RevocacionDeClientes
{
    private readonly string _ruta;
    private readonly X509Certificate2 _ca;
    private readonly ILogger _logger;
    private readonly TimeProvider _reloj;
    private readonly object _cerrojo = new();
    private byte[] _leida;
    private CrlDeClientes? _crl;
    private DateTimeOffset _ultimoAviso = DateTimeOffset.MinValue;

    /// <summary>Al arrancar, la CRL tiene que ser válida y estar vigente: si no, el servidor no arranca.</summary>
    public RevocacionDeClientes(string ruta, X509Certificate2 ca, ILogger logger, TimeProvider reloj)
    {
        _ruta = ruta;
        _ca = ca;
        _logger = logger;
        _reloj = reloj;
        try
        {
            _leida = File.ReadAllBytes(ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"No se pudo leer la CRL de clientes ({OpcionesTls.Seccion}:CrlDeClientes): {ex.Message}", ex);
        }
        _crl = CrlDeClientes.Cargar(_leida, ca);
        var ahora = reloj.GetUtcNow();
        if (_crl.Vencida(ahora))
            throw new InvalidOperationException(
                $"La CRL de clientes venció el {_crl.ProximaActualizacion:u}: emití una nueva con scripts/revocar-certificado-mtls.sh.");
        Informar(_crl, ahora);
    }

    public bool Admite(X509Certificate2 certificado)
    {
        var crl = Vigente();
        if (crl is null)
            return false;
        if (!crl.EstaRevocado(certificado))
            return true;
        _logger.LogWarning("Conexión rechazada: certificado de cliente revocado por la CA del laboratorio ({Sujeto}, {Huella}).",
            certificado.Subject, certificado.Thumbprint);
        return false;
    }

    private CrlDeClientes? Vigente()
    {
        byte[] contenido;
        try
        {
            contenido = File.ReadAllBytes(_ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogCritical(ex, "No se pudo leer la CRL de clientes: no entra ninguna workstation hasta que se pueda.");
            return null;
        }

        var ahora = _reloj.GetUtcNow();
        CrlDeClientes? crl;
        lock (_cerrojo)
        {
            if (!contenido.AsSpan().SequenceEqual(_leida))
            {
                _leida = contenido;
                try
                {
                    _crl = CrlDeClientes.Cargar(contenido, _ca);
                    Informar(_crl, ahora);
                }
                catch (InvalidOperationException ex)
                {
                    _crl = null;
                    _logger.LogCritical(ex, "La CRL de clientes nueva no es válida: no entra ninguna workstation hasta que se reemplace.");
                }
            }
            crl = _crl;
            if (crl is not null && crl.PorVencer(ahora) && !crl.Vencida(ahora) && ahora - _ultimoAviso >= TimeSpan.FromDays(1))
            {
                _ultimoAviso = ahora;
                AvisarVencimiento(crl);
            }
        }

        if (crl is null)
            return null;
        if (crl.Vencida(ahora))
        {
            _logger.LogCritical("La CRL de clientes venció el {Vencimiento:u}: no entra ninguna workstation hasta que se emita una nueva.",
                crl.ProximaActualizacion);
            return null;
        }
        return crl;
    }

    private void Informar(CrlDeClientes crl, DateTimeOffset ahora)
    {
        _logger.LogInformation("CRL de clientes cargada: {Revocados} certificados revocados, emitida el {Emitida:u}, vence el {Vence:u}.",
            crl.CantidadDeRevocados, crl.EmitidaEn, crl.ProximaActualizacion);
        if (crl.PorVencer(ahora))
        {
            _ultimoAviso = ahora;
            AvisarVencimiento(crl);
        }
    }

    private void AvisarVencimiento(CrlDeClientes crl) =>
        _logger.LogWarning("La CRL de clientes vence el {Vence:u}: emití una nueva antes, o no va a entrar ninguna workstation.",
            crl.ProximaActualizacion);
}
