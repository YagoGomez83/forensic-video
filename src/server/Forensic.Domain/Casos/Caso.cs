using Forensic.Domain.Comun;

namespace Forensic.Domain.Casos;

public enum EstadoCaso
{
    Abierto = 1,
    Cerrado = 2,
}

/// <summary>
/// Expediente que agrupa evidencias y analistas. El acceso a la evidencia se autoriza a nivel de caso.
/// </summary>
public sealed class Caso
{
    private readonly List<string> _analistasAsignados = [];

    private Caso()
    {
        NumeroExpediente = null!;
        Descripcion = null!;
        AbiertoPor = null!;
    }

    public Guid Id { get; private set; }
    public string NumeroExpediente { get; private set; }
    public string Descripcion { get; private set; }
    public EstadoCaso Estado { get; private set; }
    public DateTimeOffset AbiertoEn { get; private set; }
    public string AbiertoPor { get; private set; }
    public IReadOnlyList<string> AnalistasAsignados => _analistasAsignados;

    public static Caso Abrir(string numeroExpediente, string descripcion, Actor actor, DateTimeOffset ahora)
    {
        if (actor.Rol is Rol.Auditor or Rol.Admin)
            throw new AccesoDenegadoException($"Un {actor.Rol.ToString().ToLowerInvariant()} no puede abrir casos.");
        if (string.IsNullOrWhiteSpace(numeroExpediente) || numeroExpediente.Length > 100)
            throw new ExcepcionDeDominio("El número de expediente es obligatorio y de hasta 100 caracteres.");
        if (descripcion is null || descripcion.Length > 2000)
            throw new ExcepcionDeDominio("La descripción admite hasta 2000 caracteres.");

        var caso = new Caso
        {
            Id = Guid.NewGuid(),
            NumeroExpediente = numeroExpediente.Trim(),
            Descripcion = descripcion,
            Estado = EstadoCaso.Abierto,
            AbiertoEn = Tiempo.NormalizarParaCustodia(ahora),
            AbiertoPor = actor.Usuario,
        };
        // Un analista que abre un caso queda asignado; un supervisor asigna explícitamente.
        if (actor.Rol == Rol.Analista)
            caso._analistasAsignados.Add(actor.Usuario);
        return caso;
    }

    public void AsignarAnalista(string usuario, Actor actor)
    {
        if (actor.Rol != Rol.Supervisor)
            throw new AccesoDenegadoException("Sólo un supervisor asigna analistas.");
        ExigirAbierto();
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ExcepcionDeDominio("Usuario de analista vacío.");
        if (_analistasAsignados.Contains(usuario))
            throw new ExcepcionDeDominio($"'{usuario}' ya está asignado al caso.");
        _analistasAsignados.Add(usuario);
    }

    public void Cerrar(Actor actor)
    {
        if (actor.Rol != Rol.Supervisor)
            throw new AccesoDenegadoException("Sólo un supervisor cierra casos.");
        ExigirAbierto();
        Estado = EstadoCaso.Cerrado;
    }

    /// <summary>
    /// Supervisores y auditores leen todos los casos; un analista, sólo los asignados. El admin
    /// administra usuarios y no accede a casos.
    /// </summary>
    public bool PuedeLeer(Actor actor) => actor.Rol switch
    {
        Rol.Supervisor or Rol.Auditor => true,
        Rol.Analista => _analistasAsignados.Contains(actor.Usuario),
        _ => false,
    };

    /// <summary>Operar (ingestar, procesar) exige caso abierto y analista asignado. El auditor nunca opera.</summary>
    public bool PuedeOperar(Actor actor) =>
        Estado == EstadoCaso.Abierto
        && actor.Rol == Rol.Analista
        && _analistasAsignados.Contains(actor.Usuario);

    /// <summary>
    /// Completar un control cruzado pendiente es aditivo y no investigativo: relee un blob ya
    /// verificado y asienta el resultado del worker. Por eso, además de quien puede operar, lo puede
    /// hacer un supervisor en cualquier estado del caso, incluido cerrado. El auditor nunca escribe.
    /// </summary>
    public bool PuedeCompletarControlCruzado(Actor actor) =>
        PuedeOperar(actor) || actor.Rol == Rol.Supervisor;

    /// <summary>
    /// Emitir un informe pericial: el analista asignado con el caso abierto (informe preliminar) y un
    /// supervisor en cualquier estado. Es una decisión de política: exigir el caso cerrado sería
    /// cambiar sólo esta regla. El auditor nunca, porque la emisión se asienta en la cadena.
    /// </summary>
    public void ExigirEmisionDeInformePericial(Actor actor)
    {
        if (!(PuedeOperar(actor) || actor.Rol == Rol.Supervisor))
            throw new AccesoDenegadoException(
                $"'{actor.Usuario}' no puede emitir un informe pericial del caso {NumeroExpediente} (estado {Estado}).");
    }

    /// <summary>
    /// Emitir un informe investigativo: hoy, la misma política que el pericial; va aparte para que una
    /// pueda cambiar sin arrastrar a la otra.
    /// </summary>
    public void ExigirEmisionDeInformeInvestigativo(Actor actor)
    {
        if (!(PuedeOperar(actor) || actor.Rol == Rol.Supervisor))
            throw new AccesoDenegadoException(
                $"'{actor.Usuario}' no puede emitir un informe investigativo del caso {NumeroExpediente} (estado {Estado}).");
    }

    public void ExigirLectura(Actor actor)
    {
        if (!PuedeLeer(actor))
            throw new AccesoDenegadoException($"'{actor.Usuario}' no tiene acceso al caso {NumeroExpediente}.");
    }

    public void ExigirOperacion(Actor actor)
    {
        if (!PuedeOperar(actor))
            throw new AccesoDenegadoException(
                $"'{actor.Usuario}' no puede operar sobre el caso {NumeroExpediente} (estado {Estado}).");
    }

    public void ExigirCompletarControlCruzado(Actor actor)
    {
        if (!PuedeCompletarControlCruzado(actor))
            throw new AccesoDenegadoException(
                $"'{actor.Usuario}' no puede completar controles cruzados en el caso {NumeroExpediente} (estado {Estado}).");
    }

    private void ExigirAbierto()
    {
        if (Estado != EstadoCaso.Abierto)
            throw new ExcepcionDeDominio($"El caso {NumeroExpediente} está cerrado.");
    }
}
