using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SearchBook.Services;

public static partial class RegistrationNumberReader
{
    public static IReadOnlyList<string> Read(string path) => ReadDetailed(path).Numbers;

    public static RegistrationReadResult ReadDetailed(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("입력 파일을 찾을 수 없습니다.", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        IEnumerable<string> source = extension switch
        {
            ".txt" or ".csv" or ".tsv" => ReadTextFile(path),
            ".xlsx" => ReadXlsx(path),
            ".xls" => throw new NotSupportedException("이전 Excel 형식(.xls)은 지원하지 않습니다. Excel에서 .xlsx로 저장한 뒤 다시 선택해 주세요."),
            _ => throw new NotSupportedException(".xlsx, .txt, .csv 파일만 사용할 수 있습니다.")
        };

        return Extract(source);
    }

    public static RegistrationReadResult ReadText(string content)
    {
        using var reader = new StringReader(content);
        var values = new List<string>();
        while (reader.ReadLine() is { } line) values.Add(line);
        return Extract(values);
    }

    private static RegistrationReadResult Extract(IEnumerable<string> source)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valuesScanned = 0;
        var matchesFound = 0;
        foreach (var value in source)
        {
            valuesScanned++;
            foreach (Match match in RegistrationRegex().Matches(value.ToUpperInvariant()))
            {
                matchesFound++;
                var normalized = match.Value.Trim().ToUpperInvariant();
                if (seen.Add(normalized)) result.Add(normalized);
            }
        }

        if (result.Count == 0)
            throw new InvalidDataException("EM 또는 WM으로 시작하는 등록번호를 찾지 못했습니다.");
        return new RegistrationReadResult(
            result,
            valuesScanned,
            matchesFound,
            matchesFound - result.Count);
    }

    private static IEnumerable<string> ReadTextFile(string path)
    {
        using var reader = new StreamReader(path, DetectTextEncoding(path), detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private static Encoding DetectTextEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(949);
        }
    }

    private static IEnumerable<string> ReadXlsx(string path)
    {
        XlsxSafety.ValidateFile(path);
        using var archive = ZipFile.OpenRead(path);
        XlsxSafety.ValidateArchive(archive);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetEntry = FindFirstWorksheet(archive)
            ?? throw new InvalidDataException("Excel 파일에서 첫 번째 워크시트를 찾을 수 없습니다.");

        var document = XlsxSafety.LoadXml(sheetEntry);
        foreach (var cell in document.Descendants().Where(x => x.Name.LocalName == "c"))
        {
            var type = (string?)cell.Attribute("t");
            if (type == "inlineStr")
            {
                yield return string.Concat(cell.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value));
                continue;
            }

            var raw = cell.Elements().FirstOrDefault(x => x.Name.LocalName == "v")?.Value ?? "";
            if (type == "s" && int.TryParse(raw, out var index) && index >= 0 && index < sharedStrings.Count)
                yield return sharedStrings[index];
            else
                yield return raw;
        }
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        var document = XlsxSafety.LoadXml(entry);
        return document.Descendants().Where(x => x.Name.LocalName == "si")
            .Select(si => string.Concat(si.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value)))
            .ToList();
    }

    private static ZipArchiveEntry? FindFirstWorksheet(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relationshipsEntry is null)
            return archive.GetEntry("xl/worksheets/sheet1.xml");

        var workbook = XlsxSafety.LoadXml(workbookEntry);
        var firstSheet = workbook.Descendants().FirstOrDefault(x => x.Name.LocalName == "sheet");
        var relationId = firstSheet?.Attributes().FirstOrDefault(x => x.Name.LocalName == "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationId)) return archive.GetEntry("xl/worksheets/sheet1.xml");

        var relationships = XlsxSafety.LoadXml(relationshipsEntry);
        var target = relationships.Descendants().FirstOrDefault(x =>
            x.Name.LocalName == "Relationship" && (string?)x.Attribute("Id") == relationId)?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target)) return archive.GetEntry("xl/worksheets/sheet1.xml");

        var normalized = target.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) normalized = "xl/" + normalized;
        while (normalized.Contains("../", StringComparison.Ordinal)) normalized = normalized.Replace("../", "", StringComparison.Ordinal);
        return archive.GetEntry(normalized);
    }

    [GeneratedRegex(@"(?<![A-Z0-9])(?:EM|WM)[A-Z0-9-]{2,}(?![A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex RegistrationRegex();
}

public sealed record RegistrationReadResult(
    IReadOnlyList<string> Numbers,
    int ValuesScanned,
    int MatchesFound,
    int DuplicatesRemoved);
