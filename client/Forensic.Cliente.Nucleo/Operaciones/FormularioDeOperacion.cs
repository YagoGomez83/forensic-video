using CommunityToolkit.Mvvm.ComponentModel;

namespace Forensic.Cliente.Nucleo.Operaciones;

/// <summary>Un píxel de la imagen original marcado en el visor.</summary>
public readonly record struct PuntoDeImagen(int X, int Y);

/// <summary>Lo que el analista marcó sobre la imagen, ya en píxeles de la imagen original.</summary>
public sealed record MarcasDelVisor(Visor.Region? Region, IReadOnlyList<PuntoDeImagen> Puntos);

public enum ModoDeMarcado
{
    Ninguno,
    Region,
    Puntos,
}

/// <summary>Falta un dato o la conversión de unidades no es exacta. No llega al servidor.</summary>
public sealed class ErrorDeFormulario(string mensaje) : Exception(mensaje);

/// <summary>
/// Los parámetros de una operación del catálogo, en las unidades del servidor. El formulario no valida
/// rangos ni reglas: eso lo hace el servidor, que responde con su motivo. Sólo exige que cada dato esté
/// y que la conversión a sus unidades sea exacta.
/// </summary>
public abstract class FormularioDeOperacion : ObservableObject
{
    public abstract string Tipo { get; }
    public abstract string Titulo { get; }

    /// <summary>Si la operación necesita ver la imagen (la extracción de frame, no).</summary>
    public virtual bool NecesitaImagen => true;

    public virtual ModoDeMarcado Marcado => ModoDeMarcado.Ninguno;

    /// <summary>Lanza <see cref="ErrorDeFormulario"/> si falta un dato.</summary>
    public abstract object Parametros(MarcasDelVisor marcas);

    /// <summary>Un entero desde el editor numérico (que trabaja con decimal): sin decimales.</summary>
    protected static int Entero(decimal? valor, string campo) => PuntoFijo.AEntero(valor, 0, campo);

    public override string ToString() => Titulo;
}

/// <summary>Conversión exacta de unidades naturales a las de punto fijo del servidor.</summary>
public static class PuntoFijo
{
    /// <summary>12,5 con 2 decimales → 1250. Si sobran decimales, avisa: nunca redondea en silencio.</summary>
    public static int AEntero(decimal? valor, int decimales, string campo)
    {
        if (valor is not { } v)
            throw new ErrorDeFormulario($"Falta {campo}.");
        var escalado = v * Potencia(decimales);
        if (escalado != decimal.Truncate(escalado))
            throw new ErrorDeFormulario($"{char.ToUpperInvariant(campo[0])}{campo[1..]} admite hasta {decimales} decimales.");
        return decimal.ToInt32(escalado);
    }

    private static decimal Potencia(int decimales)
    {
        var p = 1m;
        for (var i = 0; i < decimales; i++)
            p *= 10;
        return p;
    }
}
