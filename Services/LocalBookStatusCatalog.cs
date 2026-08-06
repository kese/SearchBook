using System.IO.Compression;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SearchBook.Services;

public sealed partial class LocalBookStatusCatalog
{
    private readonly string _docsDirectory;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public LocalBookStatusCatalog(string docsDirectory)
    {
        _docsDirectory = docsDirectory;
    }

    public LocalBookStatusResult Lookup(string registrationNumber)
    {
        var normalized = registrationNumber.Trim().ToUpperInvariant();
        var rangeFileName = GetRangeFileName(normalized);
        if (rangeFileName is null)
            return LocalBookStatusResult.NotFound();

        var path = Path.Combine(_docsDirectory, rangeFileName);
        if (!File.Exists(path))
            return LocalBookStatusResult.NotFound();

        try
        {
            if (!_cache.TryGetValue(path, out var statuses))
            {
                statuses = ReadStatuses(path);
                _cache[path] = statuses;
            }

            return statuses.TryGetValue(normalized, out var status) && !string.IsNullOrWhiteSpace(status)
                ? LocalBookStatusResult.Match(status, rangeFileName)
                : LocalBookStatusResult.NotFound();
        }
        catch (Exception ex)
        {
            return LocalBookStatusResult.Unavailable(ex.GetBaseException().Message);
        }
    }

    public static string? GetRangeFileName(string registrationNumber)
    {
        var match = RegistrationNumberRegex().Match(registrationNumber.Trim());
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var numericPart))
            return null;

        var rangeStartInThousands = numericPart / 10_000 * 10;
        return $"{rangeStartInThousands}k.xlsx";
    }

    private static IReadOnlyDictionary<string, string> ReadStatuses(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheet = FindFirstWorksheet(archive)
            ?? throw new InvalidDataException("첫 번째 워크시트를 찾을 수 없습니다.");

        using var stream = worksheet.Open();
        var document = XDocument.Load(stream);
        var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int? registrationColumn = null;
        int? statusColumn = null;
        var headerFound = false;

        foreach (var row in document.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            var values = row.Elements().Where(element => element.Name.LocalName == "c")
                .Select(cell => new
                {
                    Column = GetColumnIndex((string?)cell.Attribute("r")),
                    Value = ReadCellValue(cell, sharedStrings).Trim()
                })
                .Where(cell => cell.Column >= 0)
                .ToDictionary(cell => cell.Column, cell => cell.Value);

            if (!headerFound)
            {
                foreach (var cell in values)
                {
                    var header = RemoveWhitespace(cell.Value);
                    if (header == "등록번호") registrationColumn = cell.Key;
                    if (header == "도서상태") statusColumn = cell.Key;
                }

                headerFound = registrationColumn.HasValue && statusColumn.HasValue;
                continue;
            }

            if (!values.TryGetValue(registrationColumn!.Value, out var registrationNumber) ||
                !values.TryGetValue(statusColumn!.Value, out var status))
                continue;

            var normalized = registrationNumber.Trim().ToUpperInvariant();
            if (RegistrationNumberRegex().IsMatch(normalized) && !string.IsNullOrWhiteSpace(status))
                statuses.TryAdd(normalized, status.Trim());
        }

        if (!headerFound)
            throw new InvalidDataException("'등록번호'와 '도서상태' 열을 찾을 수 없습니다.");

        return statuses;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
            return string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));

        var raw = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? "";
        return type == "s" && int.TryParse(raw, out var index) && index >= 0 && index < sharedStrings.Count
            ? sharedStrings[index]
            : raw;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants().Where(element => element.Name.LocalName == "si")
            .Select(item => string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value)))
            .ToList();
    }

    private static ZipArchiveEntry? FindFirstWorksheet(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relationshipsEntry is null)
            return archive.GetEntry("xl/worksheets/sheet1.xml");

        using var workbookStream = workbookEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var firstSheet = workbook.Descendants().FirstOrDefault(element => element.Name.LocalName == "sheet");
        var relationId = firstSheet?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationId)) return archive.GetEntry("xl/worksheets/sheet1.xml");

        using var relationshipsStream = relationshipsEntry.Open();
        var relationships = XDocument.Load(relationshipsStream);
        var target = relationships.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Relationship" && (string?)element.Attribute("Id") == relationId)?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target)) return archive.GetEntry("xl/worksheets/sheet1.xml");

        var normalized = target.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) normalized = "xl/" + normalized;
        while (normalized.Contains("../", StringComparison.Ordinal))
            normalized = normalized.Replace("../", "", StringComparison.Ordinal);
        return archive.GetEntry(normalized);
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference)) return -1;
        var column = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character)) break;
            column = column * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return column - 1;
    }

    private static string RemoveWhitespace(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    [GeneratedRegex(@"^(?:EM|WM)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegistrationNumberRegex();
}

public sealed record LocalBookStatusResult(bool Found, string BookState, string SourceFile, string Error)
{
    public static LocalBookStatusResult Match(string bookState, string sourceFile) => new(true, bookState, sourceFile, "");
    public static LocalBookStatusResult NotFound() => new(false, "", "", "");
    public static LocalBookStatusResult Unavailable(string error) => new(false, "", "", error);
}

public static class LookupFallbackResolver
{
    public static LookupData Resolve(
        string registrationNumber,
        LookupData remoteData,
        LocalBookStatusCatalog localStatusCatalog)
    {
        if (remoteData.Success) return remoteData;

        var local = localStatusCatalog.Lookup(registrationNumber);
        if (local.Found)
        {
            return new LookupData(
                true,
                local.BookState,
                "",
                "",
                "",
                "",
                "",
                "",
                $"도서관 조회 미확인 · 보조 엑셀 {local.SourceFile} 적용");
        }

        var localMessage = string.IsNullOrWhiteSpace(local.Error)
            ? "보조 엑셀에도 해당 등록번호 없음"
            : $"보조 엑셀 조회 실패: {Compact(local.Error)}";
        return remoteData with { Message = $"{remoteData.Message} · {localMessage}" };
    }

    private static string Compact(string value)
    {
        var message = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 180 ? message : message[..180] + "…";
    }
}
