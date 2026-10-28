using System.Reflection;
using PdfSharp.Fonts;

namespace Forensic.Infrastructure.Informes;

/// <summary>
/// Las fuentes de los PDF van embebidas en el ensamblado: el servidor corre en Linux, sin fuentes del
/// sistema, y PDFsharp no trae fuentes con licencia clara. Liberation Sans y Mono 2.1.5 (SIL OFL 1.1,
/// Informes/Fuentes/LICENSE-LiberationFonts.txt).
/// </summary>
internal sealed class FuentesEmbebidas : IFontResolver
{
    public const string Texto = "Liberation Sans";
    public const string Monoespaciada = "Liberation Mono";

    private static readonly object Cerrojo = new();
    private static bool _instaladas;

    /// <summary>La configuración de fuentes de PDFsharp es global: se instala una sola vez por proceso.</summary>
    public static void Instalar()
    {
        lock (Cerrojo)
        {
            if (_instaladas)
                return;
            GlobalFontSettings.FontResolver = new FuentesEmbebidas();
            _instaladas = true;
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        familyName.Equals(Monoespaciada, StringComparison.OrdinalIgnoreCase)
            ? new FontResolverInfo("LiberationMono-Regular")
            : new FontResolverInfo(bold ? "LiberationSans-Bold" : "LiberationSans-Regular");

    public byte[] GetFont(string faceName)
    {
        using var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Fuentes.{faceName}.ttf")
            ?? throw new InvalidOperationException($"Falta la fuente embebida {faceName}.");
        using var copia = new MemoryStream();
        recurso.CopyTo(copia);
        return copia.ToArray();
    }
}
