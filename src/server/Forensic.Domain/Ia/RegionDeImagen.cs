using Forensic.Domain.Comun;

namespace Forensic.Domain.Ia;

/// <summary>Rectángulo en píxeles enteros de la imagen analizada, tal como la decodificó el worker.</summary>
public sealed record RegionDeImagen
{
    private RegionDeImagen(int x, int y, int ancho, int alto) => (X, Y, Ancho, Alto) = (x, y, ancho, alto);

    public int X { get; }
    public int Y { get; }
    public int Ancho { get; }
    public int Alto { get; }

    public static RegionDeImagen Crear(int x, int y, int ancho, int alto)
    {
        if (x < 0 || y < 0)
            throw new ExcepcionDeDominio("La región empieza en píxeles no negativos.");
        if (ancho <= 0 || alto <= 0)
            throw new ExcepcionDeDominio("La región no puede ser vacía.");
        return new RegionDeImagen(x, y, ancho, alto);
    }
}
