using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Evidencias;

namespace Forensic.Domain.Tests;

internal static class Fixtures
{
    public static readonly DateTimeOffset Ahora = new(2026, 10, 22, 14, 30, 0, TimeSpan.FromHours(-3));
    public static readonly Actor Ana = new("ana", Rol.Analista);
    public static readonly Actor Beto = new("beto", Rol.Analista);
    public static readonly Actor Supervisora = new("sofia", Rol.Supervisor);
    public static readonly Actor Auditor = new("aurelio", Rol.Auditor);

    public static Caso CasoDeAna() => Caso.Abrir("EXP-2026-001", "Robo en Av. Siempreviva", Ana, Ahora);

    public static HashSha256 HashDe(string contenido) => HashSha256.Calcular(System.Text.Encoding.UTF8.GetBytes(contenido));

    public static Evidencia Original(Caso caso) => Evidencia.Ingestar(caso, HashDe("video"), 1024, "cam1.mp4", Ana, Ahora);
}
