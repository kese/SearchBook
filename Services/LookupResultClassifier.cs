using SearchBook.Models;

namespace SearchBook.Services;

public static class LookupResultClassifier
{
    public const string Pending = "대기";
    public const string Running = "조회 중";
    public const string Live = "실시간 확인";
    public const string LocalSnapshot = "로컬 스냅샷";
    public const string Unconfirmed = "미확인";
    public const string Canceled = "중지됨";

    public static string GetQueryState(LookupData data)
    {
        if (!data.Success) return Unconfirmed;
        return data.DataSource.Contains("실시간", StringComparison.Ordinal)
            ? Live
            : LocalSnapshot;
    }

    public static bool ShouldRetry(BookResult row) =>
        row.QueryState is Pending or Running or Unconfirmed or Canceled;
}
