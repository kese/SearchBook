using SearchBook.Models;

namespace SearchBook.Services;

public static class LookupResultClassifier
{
    public const string Pending = "대기";
    public const string Running = "조회중";
    public const string Success = "성공";
    public const string Failed = "실패";

    public static string GetQueryState(LookupData data) =>
        data.Success ? Success : Failed;

    public static bool ShouldRetry(BookResult row) =>
        row.QueryState is Pending or Running or Failed or "조회 중" or "미확인" or "중지됨";
}
