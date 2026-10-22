using System.Text.Json;

namespace Forensic.Application.Custodia;

internal static class DetalleDeAsiento
{
    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Serializa el detalle una sola vez; lo que se hashea y verifica es el string almacenado,
    /// así que no depende de que el serializador sea estable entre versiones.
    /// </summary>
    public static string Serializar<T>(T detalle) => JsonSerializer.Serialize(detalle, Opciones);
}
