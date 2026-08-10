using System.IO.Compression;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SearchBook.Services;

public sealed partial class LocalBookStatusCatalog
{
    private readonly string _docsDirectory;
    private readonly Dictionary<string, IReadOnlyDictionary<string, LocalBookRecord>> _cache =
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
            if (!_cache.TryGetValue(path, out var records))
            {
                records = ReadRecords(path);
                _cache[path] = records;
            }

            return records.TryGetValue(normalized, out var record)
                ? LocalBookStatusResult.Match(record, rangeFileName)
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

    private static IReadOnlyDictionary<string, LocalBookRecord> ReadRecords(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheet = FindFirstWorksheet(archive)
            ?? throw new InvalidDataException("첫 번째 워크시트를 찾을 수 없습니다.");

        using var stream = worksheet.Open();
        var document = XDocument.Load(stream);
        var records = new Dictionary<string, LocalBookRecord>(StringComparer.OrdinalIgnoreCase);
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                    if (header is "등록번호" or "도서상태" or "소장위치" or "청구기호" or "서명" or
                        "저자" or "출판사" or "출판년" or "ISBN" or "최종변경일" or "기준일")
                        columns[header] = cell.Key;
                }

                headerFound = columns.ContainsKey("등록번호") && columns.ContainsKey("도서상태");
                continue;
            }

            var registrationNumber = GetValue(values, columns, "등록번호");

            var normalized = registrationNumber.Trim().ToUpperInvariant();
            if (!RegistrationNumberRegex().IsMatch(normalized)) continue;

            records.TryAdd(normalized, new LocalBookRecord(
                GetValue(values, columns, "도서상태"),
                GetValue(values, columns, "소장위치"),
                GetValue(values, columns, "청구기호"),
                GetValue(values, columns, "서명"),
                GetValue(values, columns, "저자"),
                GetValue(values, columns, "출판사"),
                GetValue(values, columns, "출판년"),
                GetValue(values, columns, "ISBN"),
                GetValue(values, columns, "최종변경일"),
                GetValue(values, columns, "기준일")));
        }

        if (!headerFound)
            throw new InvalidDataException("'등록번호'와 '도서상태' 열을 찾을 수 없습니다.");

        return records;
    }

    private static string GetValue(
        IReadOnlyDictionary<int, string> values,
        IReadOnlyDictionary<string, int> columns,
        string header) =>
        columns.TryGetValue(header, out var column) && values.TryGetValue(column, out var value)
            ? value.Trim()
            : "";

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

public sealed record LocalBookRecord(
    string BookState,
    string Location,
    string CallNumber,
    string Title,
    string Author,
    string Publisher,
    string PublicationYear,
    string Isbn,
    string LastChanged,
    string SnapshotDate)
{
    public string BuildBibliographicInfo()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Author)) parts.Add($"저자: {Author}");
        if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add($"출판사: {Publisher}");
        if (!string.IsNullOrWhiteSpace(PublicationYear)) parts.Add($"출판년: {PublicationYear}");
        if (!string.IsNullOrWhiteSpace(Isbn)) parts.Add($"ISBN: {Isbn}");
        return string.Join(" · ", parts);
    }
}

public sealed record LocalBookStatusResult(bool Found, LocalBookRecord? Record, string SourceFile, string Error)
{
    public string BookState => Record?.BookState ?? "";
    public static LocalBookStatusResult Match(LocalBookRecord record, string sourceFile) => new(true, record, sourceFile, "");
    public static LocalBookStatusResult NotFound() => new(false, null, "", "");
    public static LocalBookStatusResult Unavailable(string error) => new(false, null, "", error);
}

public static class LookupFallbackResolver
{
    public static LookupData Resolve(
        string registrationNumber,
        LookupData remoteData,
        LocalBookStatusCatalog localStatusCatalog)
    {
        var local = localStatusCatalog.Lookup(registrationNumber);
        if (local.Found && local.Record is { } record)
        {
            var sourceDescription = string.IsNullOrWhiteSpace(record.SnapshotDate)
                ? $"로컬 전체목록 {local.SourceFile}"
                : $"로컬 전체목록 {local.SourceFile} (기준일 {record.SnapshotDate})";

            if (remoteData.Success)
            {
                return remoteData with
                {
                    BookState = Prefer(remoteData.BookState, record.BookState),
                    Location = Prefer(remoteData.Location, record.Location),
                    CallNumber = Prefer(remoteData.CallNumber, record.CallNumber),
                    Title = Prefer(remoteData.Title, record.Title),
                    BibliographicInfo = Prefer(remoteData.BibliographicInfo, record.BuildBibliographicInfo()),
                    Author = Prefer(remoteData.Author, record.Author),
                    Publisher = Prefer(remoteData.Publisher, record.Publisher),
                    PublicationYear = Prefer(remoteData.PublicationYear, record.PublicationYear),
                    Isbn = Prefer(remoteData.Isbn, record.Isbn),
                    CatalogLastChanged = Prefer(remoteData.CatalogLastChanged, record.LastChanged),
                    DataSource = "도서관 실시간 조회 + 로컬 전체목록",
                    Message = $"{remoteData.Message} · {sourceDescription}으로 빈 정보 보완"
                };
            }

            return new LookupData(
                true,
                record.BookState,
                "",
                record.Location,
                record.CallNumber,
                record.Title,
                record.BuildBibliographicInfo(),
                "",
                $"도서관 조회 미확인 · {sourceDescription} 적용",
                record.Author,
                record.Publisher,
                record.PublicationYear,
                record.Isbn,
                record.LastChanged,
                "로컬 전체목록");
        }

        if (remoteData.Success)
            return string.IsNullOrWhiteSpace(remoteData.DataSource)
                ? remoteData with { DataSource = "도서관 실시간 조회" }
                : remoteData;

        var localMessage = string.IsNullOrWhiteSpace(local.Error)
            ? "로컬 전체목록에도 해당 등록번호 없음"
            : $"로컬 전체목록 조회 실패: {Compact(local.Error)}";
        return remoteData with { Message = $"{remoteData.Message} · {localMessage}" };
    }

    private static string Prefer(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static string Compact(string value)
    {
        var message = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 180 ? message : message[..180] + "…";
    }
}
