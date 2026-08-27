using System.Text.RegularExpressions;

namespace SearchBook.Services;

public static partial class ReferenceComparisonClassifier
{
    public const string Pending = "비교 대기";
    public const string Match = "일치";
    public const string Mismatch = "불일치";
    public const string Partial = "부분 확인";
    public const string ReferenceMissing = "기준 없음";
    public const string LiveUnavailable = "실시간 미확인";
    public const string NotComparable = "비교불가";

    private static readonly ComparisonField[] Fields =
    [
        new("도서상태", data => data.BookState, record => record.BookState, true),
        new("소장위치", data => data.Location, record => record.Location, true),
        new("청구기호", data => data.CallNumber, record => record.CallNumber, true),
        new("서명", data => data.Title, record => record.Title, true),
        new("저자", data => data.Author, record => record.Author, false),
        new("출판사", data => data.Publisher, record => record.Publisher, false),
        new("출판년", data => data.PublicationYear, record => record.PublicationYear, false),
        new("ISBN", data => data.Isbn, record => record.Isbn, false)
    ];

    public static ReferenceComparisonResult Compare(
        LookupData liveData,
        LocalBookStatusResult reference)
    {
        var source = string.IsNullOrWhiteSpace(reference.SourceFile) ? "기준 Excel" : reference.SourceFile;

        if (!liveData.Success)
        {
            if (reference.Found)
                return new(LiveUnavailable, $"{source}에 기준 행이 있으나 실시간 조회가 확인되지 않았습니다.", 0, 0);

            return string.IsNullOrWhiteSpace(reference.Error)
                ? new(NotComparable, "실시간 조회 실패와 기준 행 없음으로 비교할 수 없습니다.", 0, 0)
                : new(NotComparable, $"기준 Excel 조회 실패: {Compact(reference.Error)}", 0, 0);
        }

        if (!reference.Found || reference.Record is not { } record)
        {
            return string.IsNullOrWhiteSpace(reference.Error)
                ? new(ReferenceMissing, "실시간 조회는 확인했지만 기준 Excel에 해당 등록번호가 없습니다.", 0, 0)
                : new(NotComparable, $"기준 Excel 조회 실패: {Compact(reference.Error)}", 0, 0);
        }

        var matched = new List<string>();
        var missingRequired = new List<string>();
        var missingOptional = new List<string>();
        var mismatches = new List<string>();

        foreach (var field in Fields)
        {
            var expected = field.ReferenceValue(record).Trim();
            if (string.IsNullOrWhiteSpace(expected)) continue;

            var actual = field.LiveValue(liveData).Trim();
            if (string.IsNullOrWhiteSpace(actual))
            {
                (field.Required ? missingRequired : missingOptional).Add(field.Name);
                continue;
            }

            if (Normalize(field.Name, expected) == Normalize(field.Name, actual))
            {
                matched.Add(field.Name);
                continue;
            }

            mismatches.Add($"{field.Name}: 기준 '{Compact(expected)}' / 조회 '{Compact(actual)}'");
        }

        if (mismatches.Count > 0)
        {
            return new(
                Mismatch,
                $"{source} · " + string.Join("; ", mismatches),
                matched.Count,
                mismatches.Count);
        }

        if (missingRequired.Count > 0)
        {
            var detail = $"{source} · 일치 {matched.Count}개 · 핵심 항목 조회값 없음: " +
                         string.Join(", ", missingRequired);
            if (missingOptional.Count > 0)
                detail += $" · 선택 항목 조회값 없음: {string.Join(", ", missingOptional)}";
            return new(Partial, detail, matched.Count, 0);
        }

        if (matched.Count == 0)
            return new(NotComparable, $"{source} · 비교 가능한 값이 없습니다.", 0, 0);

        var suffix = missingOptional.Count == 0
            ? ""
            : $" · 선택 항목 미비교: {string.Join(", ", missingOptional)}";
        return new(Match, $"{source} · 핵심 항목 일치{suffix}", matched.Count, 0);
    }

    private static string Normalize(string fieldName, string value)
    {
        var normalized = WhitespaceRegex().Replace(value.Trim(), " ");
        if (fieldName == "소장위치")
            normalized = Regex.Replace(normalized, @"\s*/\s*", "/", RegexOptions.CultureInvariant);
        else if (fieldName == "ISBN")
            normalized = normalized.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return normalized.ToUpperInvariant();
    }

    private static string Compact(string value)
    {
        var compact = WhitespaceRegex().Replace(value.Replace('\r', ' ').Replace('\n', ' '), " ").Trim();
        return compact.Length <= 120 ? compact : compact[..120] + "…";
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record ComparisonField(
        string Name,
        Func<LookupData, string> LiveValue,
        Func<LocalBookRecord, string> ReferenceValue,
        bool Required);
}

public sealed record ReferenceComparisonResult(
    string Status,
    string Details,
    int ComparedFieldCount,
    int MismatchCount);
