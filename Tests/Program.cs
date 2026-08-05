using System.IO.Compression;
using System.Text;
using SearchBook.Models;
using SearchBook.Services;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var artifacts = Path.Combine(root, "TestArtifacts");
Directory.CreateDirectory(artifacts);
var failures = new List<string>();

Run("검색 결과 CID와 지점 파싱", () =>
{
    var html = ReadHtml(Path.Combine(root, "site-result-idid.html"));
    Assert(LibraryClient.ParseCids(html).Contains("728530"), "cid=728530을 찾지 못함");
    Assert(LibraryClient.ParseBranches(html, "728530").Contains("01"), "지점 01을 찾지 못함");
    var (title, meta) = LibraryClient.ParseBibliographic(html, "728530");
    Assert(title.Contains("인생의 역사"), $"서명 파싱 실패: {title}");
    Assert(!string.IsNullOrWhiteSpace(meta), "서지정보가 비어 있음");
});

Run("소장정보 행 파싱", () =>
{
    var html = ReadHtml(Path.Combine(root, "site-item-sample.html"));
    var items = LibraryClient.ParseItems(html);
    Assert(items.Count == 2, $"예상 2행, 실제 {items.Count}행");
    var item = items.Single(x => x.RegistrationNumber == "EM00510342");
    Assert(item.BookState.Contains("대출"), $"도서상태 파싱 실패: {item.BookState}");
    Assert(!string.IsNullOrWhiteSpace(item.Location), "소장위치가 비어 있음");
});

Run("TXT 등록번호 추출과 중복 제거", () =>
{
    var path = Path.Combine(artifacts, "input-sample.txt");
    File.WriteAllText(path, "등록번호\nEM00510342\nem00510342\n메모, WM00000001\n무시할 값", new UTF8Encoding(false));
    var values = RegistrationNumberReader.Read(path);
    Assert(values.SequenceEqual(["EM00510342", "WM00000001"]), string.Join(",", values));
});

Run("XLSX 쓰기/읽기 왕복", () =>
{
    var path = Path.Combine(artifacts, "roundtrip.xlsx");
    XlsxExporter.Write(path,
    [
        new BookResult { Sequence = 1, RegistrationNumber = "EM00510342", QueryState = "성공", BookState = "대출가능", Title = "인생의 역사" },
        new BookResult { Sequence = 2, RegistrationNumber = "WM00000001", QueryState = "미확인", Message = "테스트" }
    ]);
    using (var archive = ZipFile.OpenRead(path))
    {
        Assert(archive.GetEntry("xl/worksheets/sheet1.xml") is not null, "sheet1.xml 없음");
        Assert(archive.GetEntry("xl/styles.xml") is not null, "styles.xml 없음");
    }
    var values = RegistrationNumberReader.Read(path);
    Assert(values.SequenceEqual(["EM00510342", "WM00000001"]), "XLSX 왕복 등록번호 불일치");
});

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    await RunAsync("실제 도서관 조회", async () =>
    {
        using var client = new LibraryClient(new LookupSettings(300, 2, 50));
        var result = await client.LookupAsync("EM00510342", CancellationToken.None);
        Assert(result.Success, result.Message);
        Assert(result.BookState.Contains("대출"), $"실제 도서상태가 이상함: {result.BookState}");
        Assert(result.Title.Contains("인생의 역사"), $"실제 서명이 이상함: {result.Title}");
        Console.WriteLine($"    상태={result.BookState}, 위치={result.Location}, 서명={result.Title}");
    });
}

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("PASS: 모든 테스트가 통과했습니다.");
    return 0;
}

Console.Error.WriteLine($"FAIL: {failures.Count}개 테스트 실패");
foreach (var failure in failures) Console.Error.WriteLine("  - " + failure);
return 1;

void Run(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine("[PASS] " + name);
    }
    catch (Exception ex)
    {
        failures.Add(name + ": " + ex.Message);
        Console.WriteLine("[FAIL] " + name);
    }
}

async Task RunAsync(string name, Func<Task> action)
{
    try
    {
        await action();
        Console.WriteLine("[PASS] " + name);
    }
    catch (Exception ex)
    {
        failures.Add(name + ": " + ex.GetBaseException().Message);
        Console.WriteLine("[FAIL] " + name);
    }
}

static string ReadHtml(string path)
{
    var bytes = File.ReadAllBytes(path);
    try { return new UTF8Encoding(false, true).GetString(bytes); }
    catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(bytes); }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
