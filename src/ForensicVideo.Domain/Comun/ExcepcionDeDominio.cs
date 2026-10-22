namespace ForensicVideo.Domain.Comun;

/// <summary>Violación de una regla del dominio forense.</summary>
public class ExcepcionDeDominio(string mensaje) : Exception(mensaje);

/// <summary>El actor no tiene permiso para la acción sobre el caso.</summary>
public sealed class AccesoDenegadoException(string mensaje) : ExcepcionDeDominio(mensaje);
