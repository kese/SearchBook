using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using SearchBook.Models;

namespace SearchBook.Services;

public static class ResultWorkbookReader
{
    public static IReadOnlyList<BookResult> Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("체크포인트 파일을 찾을 수 없습니다.", path);
        if (!Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SearchBook이 저장한 .xlsx 결과 파일만 재개할 수 있습니다.");

        XlsxSafety.ValidateFile(path);
        using var archive = ZipFile.OpenRead(path);
        XlsxSafety.ValidateArchive(archive);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheet = FindFirstWorksheet(archive)
            ?? throw new InvalidDataException("Excel 파일에서 첫 번째 워크시트를 찾을 수 없습니다.");

        var document = XlsxSafety.LoadXml(worksheet);
        var rows = document.Descendants().Where(element => element.Name.LocalName == "row").ToList();
        if (rows.Count == 0) throw new InvalidDataException("Excel 결과 파일에 행이 없습니다.");

        var headers = ReadRow(rows[0], sharedStrings)
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Value))
            .ToDictionary(cell => cell.Value.Trim(), cell => cell.Column, StringComparer.OrdinalIgnoreCase);
        RequireHeader(headers, "등록번호");
        RequireHeader(headers, "조회결과");

        var results = new List<BookResult>();
        foreach (var row in rows.Skip(1))
        {
            var values = ReadRow(row, sharedStrings).ToDictionary(cell => cell.Column, cell => cell.Value);
            var registrationNumber = Get(values, headers, "등록번호").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(registrationNumber)) continue;

            var sequenceText = Get(values, headers, "입력순번");
            var sequence = int.TryParse(sequenceText, out var parsedSequence)
                ? parsedSequence
                : results.Count + 1;
            var dataSource = Get(values, headers, "정보출처");
            var queryState = NormalizeQueryState(
                Get(values, headers, "조회결과", LookupResultClassifier.Pending));
            results.Add(new BookResult
            {
                Sequence = sequence,
                RegistrationNumber = registrationNumber,
                QueryState = queryState,
                BookState = Get(values, headers, "도서상태"),
                ReturnDue = Get(values, headers, "반납예정일/기한"),
                Location = Get(values, headers, "소장위치"),
                CallNumber = Get(values, headers, "청구기호"),
                Title = Get(values, headers, "서명"),
                Author = Get(values, headers, "저자"),
                Publisher = Get(values, headers, "출판사"),
                PublicationYear = Get(values, headers, "출판년"),
                Isbn = Get(values, headers, "ISBN"),
                CatalogLastChanged = Get(values, headers, "최종변경일"),
                BibliographicInfo = Get(values, headers, "서지정보"),
                DetailUrl = Get(values, headers, "상세URL"),
                DataSource = dataSource,
                ReferenceComparison = Get(values, headers, "기준 Excel 비교", ReferenceComparisonClassifier.Pending),
                ReferenceComparisonDetails = Get(values, headers, "기준 비교 세부사항"),
                Message = Get(values, headers, "처리메시지"),
                CheckedAt = ParseCheckedAt(Get(values, headers, "조회시각"))
            });
        }

        if (results.Count == 0)
            throw new InvalidDataException("재개할 등록번호 결과 행을 찾지 못했습니다.");
        return results;
    }

    private static IReadOnlyList<SheetCell> ReadRow(XElement row, IReadOnlyList<string> sharedStrings) =>
        row.Elements().Where(element => element.Name.LocalName == "c")
            .Select(cell => new SheetCell(
                GetColumnIndex((string?)cell.Attribute("r")),
                ReadCellValue(cell, sharedStrings)))
            .Where(cell => cell.Column >= 0)
            .ToList();

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

    private static string Get(
        IReadOnlyDictionary<int, string> values,
        IReadOnlyDictionary<string, int> headers,
        string header,
        string defaultValue = "") =>
        headers.TryGetValue(header, out var column) && values.TryGetValue(column, out var value)
            ? value
            : defaultValue;

    private static DateTime? ParseCheckedAt(string value) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    private static string NormalizeQueryState(string queryState) =>
        queryState.Trim() switch
        {
            "" or LookupResultClassifier.Pending => LookupResultClassifier.Pending,
            "성공" or "실시간 확인" or "로컬 스냅샷" => LookupResultClassifier.Success,
            "실패" or "미확인" or "오류" => LookupResultClassifier.Failed,
            "조회중" or "조회 중" or "중지됨" => LookupResultClassifier.Pending,
            _ => LookupResultClassifier.Failed
        };

    private static void RequireHeader(IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.ContainsKey(name))
            throw new InvalidDataException($"SearchBook 결과 파일에 '{name}' 열이 없습니다.");
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        var document = XlsxSafety.LoadXml(entry);
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

        var workbook = XlsxSafety.LoadXml(workbookEntry);
        var firstSheet = workbook.Descendants().FirstOrDefault(element => element.Name.LocalName == "sheet");
        var relationId = firstSheet?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationId)) return archive.GetEntry("xl/worksheets/sheet1.xml");

        var relationships = XlsxSafety.LoadXml(relationshipsEntry);
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

    private sealed record SheetCell(int Column, string Value);
}
