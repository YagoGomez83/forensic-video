namespace Forensic.Domain.Comun;

public static class Tiempo
{
    /// <summary>
    /// Normaliza a UTC con precisión de microsegundos, la que guarda PostgreSQL. Sin esto, un
    /// timestamp con ticks de 100 ns cambiaría al persistirse y el hash del asiento dejaría de verificar.
    /// </summary>
    public static DateTimeOffset NormalizarParaCustodia(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
}
