using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SearchBook.Models;

namespace SearchBook.Services;

public static class DiagnosticReportWriter
{
    public static string Write(
        AppDataPaths paths,
        string version,
        string inputDisplayName,
        IReadOnlyList<BookResult> rows)
    {
        paths.EnsureCreated();
        var path = Path.Combine(
            paths.DiagnosticsDirectory,
            $"SearchBook_진단_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        var live = rows.Count(row => row.QueryState == LookupResultClassifier.Live);
        var local = rows.Count(row => row.QueryState == LookupResultClassifier.LocalSnapshot);
        var retry = rows.Count(LookupResultClassifier.ShouldRetry);

        var content = new StringBuilder()
            .AppendLine("SearchBook 진단 정보")
            .AppendLine("====================")
            .AppendLine($"버전: {version}")
            .AppendLine($"운영체제: {RuntimeInformation.OSDescription}")
            .AppendLine($"런타임: {RuntimeInformation.FrameworkDescription}")
            .AppendLine($"입력 표시명: {Path.GetFileName(inputDisplayName)}")
            .AppendLine($"전체 결과: {rows.Count:N0}")
            .AppendLine($"실시간 확인: {live:N0}")
            .AppendLine($"로컬 스냅샷: {local:N0}")
            .AppendLine($"재조회 대상: {retry:N0}")
            .AppendLine($"사용자 데이터 폴더: {paths.RootDirectory}")
            .AppendLine($"생성 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .ToString();
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
