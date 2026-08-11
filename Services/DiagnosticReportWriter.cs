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
        var success = rows.Count(row => row.QueryState == LookupResultClassifier.Success);
        var failed = rows.Count(row => row.QueryState == LookupResultClassifier.Failed);
        var pending = rows.Count(row => row.QueryState == LookupResultClassifier.Pending);
        var running = rows.Count(row => row.QueryState == LookupResultClassifier.Running);

        var content = new StringBuilder()
            .AppendLine("SearchBook 진단 정보")
            .AppendLine("====================")
            .AppendLine($"버전: {version}")
            .AppendLine($"운영체제: {RuntimeInformation.OSDescription}")
            .AppendLine($"런타임: {RuntimeInformation.FrameworkDescription}")
            .AppendLine($"입력 표시명: {Path.GetFileName(inputDisplayName)}")
            .AppendLine($"전체 결과: {rows.Count:N0}")
            .AppendLine($"성공: {success:N0}")
            .AppendLine($"실패: {failed:N0}")
            .AppendLine($"대기: {pending:N0}")
            .AppendLine($"조회중: {running:N0}")
            .AppendLine($"사용자 데이터 폴더: {paths.RootDirectory}")
            .AppendLine($"생성 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .ToString();
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
