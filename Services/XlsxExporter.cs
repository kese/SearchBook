using System.IO.Compression;
using System.IO;
using System.Text;
using System.Xml;
using SearchBook.Models;

namespace SearchBook.Services;

public static class XlsxExporter
{
    private static readonly string[] Headers =
    [
        "입력순번", "등록번호", "조회결과", "도서상태", "반납예정일/기한", "소장위치",
        "청구기호", "서명", "서지정보", "상세URL", "처리메시지", "조회시각"
    ];

    public static void Write(string path, IReadOnlyList<BookResult> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tempPath = path + ".writing";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                WriteText(archive, "[Content_Types].xml", ContentTypes);
                WriteText(archive, "_rels/.rels", RootRelationships);
                WriteText(archive, "docProps/app.xml", AppProperties);
                WriteCoreProperties(archive);
                WriteText(archive, "xl/workbook.xml", Workbook);
                WriteText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
                WriteText(archive, "xl/styles.xml", Styles);
                WriteWorksheet(archive, rows);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public static void WriteTemplate(string path)
    {
        var rows = new[]
        {
            new BookResult { Sequence = 1, RegistrationNumber = "EM00510342" },
            new BookResult { Sequence = 2, RegistrationNumber = "WM00000001" }
        };
        Write(path, rows);
    }

    private static void WriteWorksheet(ZipArchive archive, IReadOnlyList<BookResult> rows)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        writer.WriteStartDocument(true);
        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("sheetViews");
        writer.WriteStartElement("sheetView");
        writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane");
        writer.WriteAttributeString("ySplit", "1");
        writer.WriteAttributeString("topLeftCell", "A2");
        writer.WriteAttributeString("activePane", "bottomLeft");
        writer.WriteAttributeString("state", "frozen");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cols");
        var widths = new[] { 10d, 18, 12, 15, 18, 20, 22, 42, 36, 46, 34, 21 };
        for (var i = 0; i < widths.Length; i++)
        {
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", (i + 1).ToString());
            writer.WriteAttributeString("max", (i + 1).ToString());
            writer.WriteAttributeString("width", widths[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        writer.WriteStartElement("sheetData");
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", "1");
        writer.WriteAttributeString("ht", "26");
        writer.WriteAttributeString("customHeight", "1");
        for (var i = 0; i < Headers.Length; i++) WriteCell(writer, 1, i + 1, Headers[i], 1);
        writer.WriteEndElement();

        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 2;
            var row = rows[i];
            writer.WriteStartElement("row");
            writer.WriteAttributeString("r", rowNumber.ToString());
            var values = new[]
            {
                row.Sequence.ToString(), row.RegistrationNumber, row.QueryState, row.BookState,
                row.ReturnDue, row.Location, row.CallNumber, row.Title, row.BibliographicInfo,
                row.DetailUrl, row.Message, row.CheckedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
            };
            for (var c = 0; c < values.Length; c++) WriteCell(writer, rowNumber, c + 1, values[c], 0);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        var lastRow = Math.Max(1, rows.Count + 1);
        writer.WriteStartElement("autoFilter");
        writer.WriteAttributeString("ref", $"A1:L{lastRow}");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteCell(XmlWriter writer, int row, int column, string? value, int style)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", ColumnName(column) + row);
        writer.WriteAttributeString("t", "inlineStr");
        if (style != 0) writer.WriteAttributeString("s", style.ToString());
        writer.WriteStartElement("is");
        writer.WriteStartElement("t");
        writer.WriteAttributeString("xml", "space", null, "preserve");
        writer.WriteString(SanitizeXml(value ?? ""));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static string ColumnName(int number)
    {
        var name = "";
        while (number > 0)
        {
            number--;
            name = (char)('A' + number % 26) + name;
            number /= 26;
        }
        return name;
    }

    private static string SanitizeXml(string value) => new(value.Where(ch =>
        ch == '\t' || ch == '\n' || ch == '\r' || ch >= ' ').ToArray());

    private static void WriteText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteCoreProperties(ZipArchive archive)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        WriteText(archive, "docProps/core.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:creator>SearchBook</dc:creator><cp:lastModifiedBy>SearchBook</cp:lastModifiedBy><dcterms:created xsi:type="dcterms:W3CDTF">{now}</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">{now}</dcterms:modified></cp:coreProperties>
            """);
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/><Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/></Types>
        """;
    private const string RootRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>
        """;
    private const string Workbook = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="도서상태" sheetId="1" r:id="rId1"/></sheets></workbook>
        """;
    private const string WorkbookRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
        """;
    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="10"/><name val="Malgun Gothic"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="10"/><name val="Malgun Gothic"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF316BFF"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
        """;
    private const string AppProperties = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>SearchBook</Application></Properties>
        """;
}
