namespace Forensic.Cliente.Api;

/// <summary>
/// La sesión del analista: el JWT y su vencimiento. Vive sólo en memoria: nunca se escribe en disco,
/// así que cerrar la aplicación es cerrar la sesión.
/// </summary>
public interface ISesion
{
    string? Token { get; }
    DateTimeOffset? ExpiraEn { get; }
    bool Activa { get; }

    /// <summary>El servidor rechazó el token (401): hay que volver al login.</summary>
    event EventHandler? Expirada;

    void Iniciar(string token, DateTimeOffset expiraEn);

    /// <summary>La sesión dejó de valer por decisión del servidor. Avisa con <see cref="Expirada"/>.</summary>
    void Expirar();

    /// <summary>El analista cierra la sesión. No avisa.</summary>
    void Cerrar();
}

public sealed class SesionEnMemoria : ISesion
{
    private readonly object _cerrojo = new();

    public string? Token { get; private set; }
    public DateTimeOffset? ExpiraEn { get; private set; }
    public bool Activa => Token is not null;

    public event EventHandler? Expirada;

    public void Iniciar(string token, DateTimeOffset expiraEn)
    {
        lock (_cerrojo)
            (Token, ExpiraEn) = (token, expiraEn);
    }

    public void Expirar()
    {
        bool estabaActiva;
        lock (_cerrojo)
        {
            estabaActiva = Token is not null;
            (Token, ExpiraEn) = (null, null);
        }
        if (estabaActiva)
            Expirada?.Invoke(this, EventArgs.Empty);
    }

    public void Cerrar()
    {
        lock (_cerrojo)
            (Token, ExpiraEn) = (null, null);
    }

    /// <summary>Nunca expone el token.</summary>
    public override string ToString() => Activa ? $"sesión activa hasta {ExpiraEn:u}" : "sin sesión";
}
