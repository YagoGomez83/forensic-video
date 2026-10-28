using System.Globalization;
using Forensic.Application.Informes;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Forensic.Infrastructure.Informes;

/// <summary>
/// Redacta el PDF de un informe con MigraDoc (PDFsharp-MigraDoc 6.2.4, MIT). Sólo da forma a lo que
/// recibe: qué entra al informe ya lo decidió la aplicación.
/// </summary>
internal sealed class RedactorDePdf : IRedactorDeInformes
{
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    // Miles con punto, como se escriben en castellano; fijo, sin depender de la cultura del servidor.
    private static readonly NumberFormatInfo Miles = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public RedactorDePdf() => FuentesEmbebidas.Instalar();

    public byte[] RedactarPericial(InformePericialParaRedactar i)
    {
        var doc = NuevoDocumento($"Informe pericial {i.Caso.NumeroExpediente}", "Informe pericial");
        var s = Seccion(doc, $"Informe pericial · Expediente {i.Caso.NumeroExpediente}");

        s.AddParagraph("INFORME PERICIAL", "Titulo");
        s.AddParagraph(
            "Sólo material admisible en juicio: evidencias originales y derivados de operaciones deterministas. " +
            "El material de la vía «sólo investigación» (inteligencia artificial) no forma parte de este informe.",
            "Aclaracion");

        s.AddParagraph("1. Datos del caso y de la emisión", "Heading1");
        Datos(s,
            ("Expediente", i.Caso.NumeroExpediente),
            ("Descripción", i.Caso.Descripcion),
            ("Id del caso", i.Caso.Id.ToString("D")),
            ("Estado del caso", i.Caso.Estado.ToString()),
            ("Emitido por", i.EmitidoPor),
            ("Emitido en (UTC, servidor)", Fecha(i.EmitidoEn)),
            ("Cadena cubierta", $"asientos 1 a {i.Hasta}"),
            ("Hash de cabeza", i.HashCabeza));

        s.AddParagraph($"2. Evidencias presentadas ({i.Evidencias.Count})", "Heading1");
        foreach (var e in i.Evidencias)
            Evidencia(s, e);
        if (i.EvidenciasExcluidas > 0)
            s.AddParagraph(
                $"{i.EvidenciasExcluidas} evidencia(s) del caso son «sólo investigación» y se excluyeron: no forman parte de esta prueba.",
                "Nota");

        s.AddParagraph("3. Cadena de custodia", "Heading1");
        s.AddParagraph(
            $"La cadena entra entera para que pueda verificarse. {i.AsientosOpacos} asiento(s) excluido(s): no forman parte de esta prueba; " +
            "de ellos se muestra sólo el hash, que los mantiene encadenados.",
            "Nota");
        Cadena(s, i.Asientos);

        s.AddParagraph("4. Sello del laboratorio y verificación", "Heading1");
        Datos(s,
            ("Último sello", $"secuencia {i.UltimoSello.Secuencia}"),
            ("Hash sellado", i.UltimoSello.HashCabeza.Hex),
            ("Sellado en (UTC)", Fecha(i.UltimoSello.SelladoEn)),
            ("Clave del laboratorio", i.ClaveDelLaboratorio),
            ("Firma (Ed25519, base64)", Convert.ToBase64String(i.UltimoSello.Firma)));
        s.AddParagraph(
            "Este informe se acompaña de un bundle verificable (forensic-video/bundle/v1) que contiene este mismo PDF, la cadena, " +
            "los sellos y las evidencias. Cualquier tercero puede verificarlo sin el servidor del laboratorio con el verificador " +
            "independiente y open-source del proyecto:",
            "Normal");
        s.AddParagraph($"verificador --clave <huella> --exigir pericial [--externos <carpeta>] <bundle.zip>", "Codigo");
        s.AddParagraph(
            "La huella de la clave debe obtenerse del laboratorio por un medio independiente de este informe y compararse con la " +
            "indicada arriba. Una cadena reconstruida no puede reproducir los sellos anteriores del laboratorio.",
            "Nota");

        return Renderizar(doc);
    }

    private static Document NuevoDocumento(string titulo, string asunto)
    {
        var doc = new Document();
        doc.Info.Title = titulo;
        doc.Info.Subject = asunto;
        doc.Info.Author = "forensic-video";

        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = FuentesEmbebidas.Texto;
        normal.Font.Size = 9;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var titulo1 = doc.Styles.AddStyle("Titulo", StyleNames.Normal);
        titulo1.Font.Size = 18;
        titulo1.Font.Bold = true;
        titulo1.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

        var h1 = doc.Styles[StyleNames.Heading1]!;
        h1.Font.Name = FuentesEmbebidas.Texto;
        h1.Font.Size = 12;
        h1.Font.Bold = true;
        h1.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
        h1.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
        h1.ParagraphFormat.KeepWithNext = true;

        var aclaracion = doc.Styles.AddStyle("Aclaracion", StyleNames.Normal);
        aclaracion.Font.Bold = true;
        aclaracion.ParagraphFormat.Borders.Width = Unit.FromPoint(0.75);
        aclaracion.ParagraphFormat.Borders.Distance = Unit.FromPoint(4);
        aclaracion.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);

        var nota = doc.Styles.AddStyle("Nota", StyleNames.Normal);
        nota.Font.Italic = true;

        var codigo = doc.Styles.AddStyle("Codigo", StyleNames.Normal);
        codigo.Font.Name = FuentesEmbebidas.Monoespaciada;
        codigo.Font.Size = 7.5;
        return doc;
    }

    private static Section Seccion(Document doc, string pie)
    {
        var s = doc.AddSection();
        s.PageSetup = doc.DefaultPageSetup.Clone();
        s.PageSetup.PageFormat = PageFormat.A4;
        s.PageSetup.LeftMargin = s.PageSetup.RightMargin = Unit.FromCentimeter(2);
        s.PageSetup.TopMargin = s.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        var p = s.Footers.Primary.AddParagraph();
        p.Format.Font.Size = 7;
        p.AddText($"{pie} · página ");
        p.AddPageField();
        p.AddText(" de ");
        p.AddNumPagesField();
        return s;
    }

    private static string Fecha(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff 'UTC'", Invariante);

    private static void Datos(Section s, params (string Clave, string Valor)[] filas)
    {
        var t = s.AddTable();
        t.Borders.Width = Unit.FromPoint(0.25);
        t.AddColumn(Unit.FromCentimeter(4.5));
        t.AddColumn(Unit.FromCentimeter(12.5));
        foreach (var (clave, valor) in filas)
        {
            var fila = t.AddRow();
            fila.Cells[0].AddParagraph(clave).Format.Font.Bold = true;
            Celda(fila.Cells[1], valor, mono: EsTecnico(valor));
        }
        s.AddParagraph();
    }

    private static bool EsTecnico(string valor) =>
        valor.Length >= 32 && !valor.Contains(' ');

    private static void Celda(Cell celda, string valor, bool mono)
    {
        var p = celda.AddParagraph();
        if (mono)
        {
            p.Format.Font.Name = FuentesEmbebidas.Monoespaciada;
            p.Format.Font.Size = 7.5;
            // Cortes posibles cada 32 caracteres para que hashes y firmas no desborden la celda.
            for (var i = 0; i < valor.Length; i += 32)
            {
                if (i > 0)
                    p.AddText("​");
                p.AddText(valor.Substring(i, Math.Min(32, valor.Length - i)));
            }
        }
        else
        {
            p.AddText(valor);
        }
    }

    private static void Evidencia(Section s, EvidenciaParaRedactar e)
    {
        var filas = new List<(string, string)>
        {
            ("Nombre declarado", e.Nombre),
            ("Id", e.Id.ToString("D")),
            ("SHA-256", e.Sha256),
            ("Tamaño", $"{e.TamanoBytes.ToString("N0", Miles)} bytes"),
            ("En el bundle", e.Embebida ? "embebida" : "se entrega aparte (se verifica por hash)"),
        };
        if (e.Operacion is null)
        {
            filas.Add(("Origen", $"original, ingestado en el asiento {e.AsientoDeOrigen}"));
        }
        else
        {
            filas.Add(("Origen", $"derivado de {e.EntradaId:D} por la operación {e.Operacion}, asiento {e.AsientoDeOrigen}"));
            filas.Add(("Parámetros", e.Parametros!));
            filas.Add(("Entorno", e.Entorno!));
        }
        Datos(s, [.. filas]);
    }

    private static void Cadena(Section s, IReadOnlyList<AsientoParaRedactar> asientos)
    {
        var t = s.AddTable();
        t.Borders.Width = Unit.FromPoint(0.25);
        t.Format.Font.Size = 7.5;
        foreach (var ancho in new[] { 1.0, 3.6, 2.0, 4.2, 6.2 })
            t.AddColumn(Unit.FromCentimeter(ancho));
        var encabezado = t.AddRow();
        encabezado.HeadingFormat = true;
        encabezado.Format.Font.Bold = true;
        string[] titulos = ["Sec.", "Tipo", "Actor", "Timestamp (UTC)", "Hash"];
        for (var c = 0; c < titulos.Length; c++)
            encabezado.Cells[c].AddParagraph(titulos[c]);

        foreach (var a in asientos)
        {
            var fila = t.AddRow();
            fila.Cells[0].AddParagraph(a.Secuencia.ToString(Invariante));
            if (a.Opaco)
            {
                fila.Cells[1].MergeRight = 2;
                fila.Cells[1].AddParagraph("excluido: no forma parte de esta prueba").Format.Font.Italic = true;
            }
            else
            {
                fila.Cells[1].AddParagraph(a.Tipo!);
                fila.Cells[2].AddParagraph(a.Actor!);
                fila.Cells[3].AddParagraph(Fecha(a.Timestamp!.Value));
            }
            Celda(fila.Cells[4], a.Hash, mono: true);
        }
        s.AddParagraph();
    }

    private static byte[] Renderizar(Document doc)
    {
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var salida = new MemoryStream();
        renderer.PdfDocument.Save(salida, false);
        return salida.ToArray();
    }
}
