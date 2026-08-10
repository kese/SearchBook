using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
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
        new BookResult { Sequence = 1, RegistrationNumber = "EM00510342", QueryState = "성공", BookState = "대출가능", Title = "인생의 역사", Author = "신형철", Publisher = "난다", PublicationYear = "2022", Isbn = "9791191859379", CatalogLastChanged = "20260806", DataSource = "테스트" },
        new BookResult { Sequence = 2, RegistrationNumber = "WM00000001", QueryState = "미확인", Message = "테스트" }
    ]);
    using (var archive = ZipFile.OpenRead(path))
    {
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        Assert(sheetEntry is not null, "sheet1.xml 없음");
        Assert(stylesEntry is not null, "styles.xml 없음");
        using var sheetReader = new StreamReader(sheetEntry!.Open(), Encoding.UTF8);
        using var stylesReader = new StreamReader(stylesEntry!.Open(), Encoding.UTF8);
        var sheetXml = sheetReader.ReadToEnd();
        var stylesXml = stylesReader.ReadToEnd();
        Assert(sheetXml.Contains("r=\"L2\" t=\"inlineStr\" s=\"2\""), "ISBN 셀에 텍스트 스타일이 적용되지 않음");
        Assert(stylesXml.Contains("numFmtId=\"49\"") && stylesXml.Contains("quotePrefix=\"1\""), "식별자 텍스트 서식 정의가 없음");
    }
    var values = RegistrationNumberReader.Read(path);
    Assert(values.SequenceEqual(["EM00510342", "WM00000001"]), "XLSX 왕복 등록번호 불일치");
});

Run("보조 Excel 번호대/EM·WM 상태 조회", () =>
{
    var docs = Path.Combine(artifacts, "local-status-docs");
    Directory.CreateDirectory(docs);
    var path = Path.Combine(docs, "180k.xlsx");
    XlsxExporter.Write(path,
    [
        new BookResult { Sequence = 1, RegistrationNumber = "EM00180001", BookState = "대출가능", Location = "중앙도서관 / 보존서고", CallNumber = "811.7 신94ㅇ", Title = "인생의 역사", Author = "신형철", Publisher = "난다", PublicationYear = "2022", Isbn = "9791191859379", CatalogLastChanged = "20260806" },
        new BookResult { Sequence = 2, RegistrationNumber = "WM00180002", BookState = "소재불명", Title = "WM 테스트 도서", Author = "테스트 저자" }
    ]);

    Assert(LocalBookStatusCatalog.GetRangeFileName("EM00180001") == "180k.xlsx", "EM 번호대 파일 계산 실패");
    Assert(LocalBookStatusCatalog.GetRangeFileName("wm00180002") == "180k.xlsx", "WM 번호대 파일 계산 실패");

    var catalog = new LocalBookStatusCatalog(docs);
    var em = catalog.Lookup("EM00180001");
    var wm = catalog.Lookup("wm00180002");
    var missing = catalog.Lookup("EM00180003");
    Assert(em.Found && em.BookState == "대출가능", "EM 보조 상태 조회 실패");
    Assert(em.Record?.Title == "인생의 역사" && em.Record.Author == "신형철", "EM 보조 전체정보 조회 실패");
    Assert(wm.Found && wm.BookState == "소재불명", "WM 보조 상태 조회 실패");
    Assert(wm.Record?.Title == "WM 테스트 도서", "WM 정확 일치 전체정보 조회 실패");
    Assert(!missing.Found, "없는 등록번호가 조회됨");

    var resolved = LookupFallbackResolver.Resolve(
        "EM00180001",
        LookupData.NotFound("원격 미확인"),
        catalog);
    var unresolved = LookupFallbackResolver.Resolve(
        "EM00180003",
        LookupData.NotFound("원격 미확인"),
        catalog);
    Assert(resolved.Success && resolved.BookState == "대출가능", "원격 미확인 결과에 보조 상태가 적용되지 않음");
    Assert(resolved.Title == "인생의 역사" && resolved.Author == "신형철", "원격 미확인 결과에 보조 전체정보가 적용되지 않음");
    Assert(resolved.DataSource == "로컬 전체목록", "로컬 전체목록 출처가 기록되지 않음");
    Assert(resolved.Message.Contains("180k.xlsx"), "보조 상태 출처가 처리메시지에 없음");
    Assert(!unresolved.Success && unresolved.Message.Contains("로컬 전체목록에도"), "로컬 전체목록에도 없을 때 미확인 유지 실패");

    var merged = LookupFallbackResolver.Resolve(
        "EM00180001",
        new LookupData(true, "대출중", "2026-08-20", "", "", "원격 제목", "", "https://example.invalid", "조회 완료", DataSource: "도서관 실시간 조회"),
        catalog);
    Assert(merged.BookState == "대출중", "원격 현재 상태가 로컬 스냅샷보다 우선하지 않음");
    Assert(merged.ReturnDue == "2026-08-20", "원격 반납기한이 보존되지 않음");
    Assert(merged.Title == "원격 제목", "원격 서명이 로컬 서명보다 우선하지 않음");
    Assert(merged.Location == "중앙도서관 / 보존서고" && merged.Author == "신형철", "원격 빈 항목이 로컬 전체정보로 보완되지 않음");
    Assert(merged.DataSource.Contains("실시간") && merged.DataSource.Contains("로컬"), "병합 결과 출처가 불완전함");
});

Run("실제 docs 전체 도서정보 조회", () =>
{
    var catalog = new LocalBookStatusCatalog(Path.Combine(root, "docs"));
    var removed = catalog.Lookup("EM00182943");
    Assert(removed.Found, "docs/180k.xlsx에서 제적 도서 EM00182943을 찾지 못함");
    Assert(removed.BookState == "제적", $"예상 제적, 실제 {removed.BookState}");
    Assert(removed.Record?.Title.Contains("賃貸住宅") == true, "제적 도서 서명이 비어 있거나 다름");
    Assert(removed.Record?.Author == "박의권", "제적 도서 저자가 비어 있거나 다름");
    Assert(removed.Record?.CallNumber == "DP 335.8 박67ㅎ c.4", "제적 도서 청구기호가 비어 있거나 다름");
    Assert(removed.Record?.SnapshotDate == "2026-08-06", "로컬 목록 기준일이 다름");

    var missing = catalog.Lookup("EM00182477");
    Assert(missing.Found && missing.BookState == "소재불명", "소재불명 도서 조회 실패");
    Assert(missing.Record?.Title == "태엽감는 새" && missing.Record.Isbn.Contains("8970121269"), "소재불명 도서 전체정보 조회 실패");
});

Run(".env GitHub 토큰 로딩과 환경변수 우선순위", () =>
{
    const string variable = "SEARCHBOOK_TEST_GITHUB_TOKEN";
    var envPath = Path.Combine(artifacts, "update-token.env");
    File.WriteAllText(envPath, $"# SearchBook test\n{variable}=from-env-file\n", new UTF8Encoding(false));
    var original = Environment.GetEnvironmentVariable(variable);
    try
    {
        Environment.SetEnvironmentVariable(variable, null);
        Assert(EnvironmentFile.GetValue(variable, [envPath]) == "from-env-file", ".env 토큰 로딩 실패");
        Environment.SetEnvironmentVariable(variable, "from-process");
        Assert(EnvironmentFile.GetValue(variable, [envPath]) == "from-process", "프로세스 환경변수 우선 적용 실패");
    }
    finally
    {
        Environment.SetEnvironmentVariable(variable, original);
    }
});

await RunAsync("GitHub 업데이트 확인/다운로드/SHA-256 검증", async () =>
{
    var payload = Encoding.UTF8.GetBytes("SearchBook update fixture");
    var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    var releaseJson = $$"""
        {
          "tag_name": "v2.0.0",
          "html_url": "https://github.com/StandardChartered/SearchBook/releases/tag/v2.0.0",
          "assets": [
            {
              "name": "SearchBook-win-x64.zip",
              "url": "https://api.github.com/repos/StandardChartered/SearchBook/releases/assets/1",
              "browser_download_url": "https://github.com/example/framework.zip",
              "digest": "sha256:{{digest}}",
              "size": {{payload.Length}}
            },
            {
              "name": "SearchBook-self-contained-win-x64.zip",
              "url": "https://api.github.com/repos/StandardChartered/SearchBook/releases/assets/2",
              "browser_download_url": "https://github.com/example/self-contained.zip",
              "digest": "sha256:{{digest}}",
              "size": {{payload.Length}}
            }
          ]
        }
        """;
    using var service = new GitHubUpdateService(new StubHttpMessageHandler(request =>
    {
        if (request.RequestUri?.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal) == true)
            return TextResponse(HttpStatusCode.OK, releaseJson, "application/json");
        return ByteResponse(HttpStatusCode.OK, payload, "application/octet-stream");
    }));

    var result = await service.CheckAsync(new Version(1, 0, 0));
    Assert(result.Status == UpdateCheckStatus.UpdateAvailable, $"업데이트 판정 실패: {result.Status}");
    Assert(result.Asset?.Name == "SearchBook-self-contained-win-x64.zip", "self-contained 자산 우선 선택 실패");

    var downloadPath = Path.Combine(artifacts, "github-update", result.Asset!.Name);
    var savedPath = await service.DownloadAsync(result.Asset, downloadPath);
    Assert(File.ReadAllBytes(savedPath).SequenceEqual(payload), "다운로드 파일 내용 불일치");
});

await RunAsync("private GitHub 릴리즈 인증 안내", async () =>
{
    using var service = new GitHubUpdateService(new StubHttpMessageHandler(_ =>
        TextResponse(HttpStatusCode.NotFound, "{}", "application/json")));
    var result = await service.CheckAsync(new Version(1, 0, 0));
    Assert(result.Status == UpdateCheckStatus.AuthenticationRequired, $"인증 필요 판정 실패: {result.Status}");
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

if (args.Contains("--github-live", StringComparer.OrdinalIgnoreCase))
{
    await RunAsync("실제 GitHub private 릴리즈 확인/다운로드", async () =>
    {
        var token = EnvironmentFile.GetGitHubToken();
        Assert(!string.IsNullOrWhiteSpace(token), "SEARCHBOOK_GITHUB_TOKEN이 없습니다.");
        using var service = new GitHubUpdateService(token);
        var result = await service.CheckAsync(new Version(0, 0, 0));
        Assert(result.Status == UpdateCheckStatus.UpdateAvailable, $"GitHub 업데이트 판정 실패: {result.Status} / {result.Message}");
        Assert(result.Asset is not null, "다운로드 가능한 Windows ZIP이 없습니다.");
        var path = Path.Combine(artifacts, "github-live", result.Asset!.Name);
        var saved = await service.DownloadAsync(result.Asset, path);
        Assert(File.Exists(saved), "GitHub 릴리즈 자산이 저장되지 않았습니다.");
        Console.WriteLine($"    릴리즈={result.TagName}, 자산={result.Asset.Name}");
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

static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string content, string contentType) => new(statusCode)
{
    Content = new StringContent(content, Encoding.UTF8, contentType)
};

static HttpResponseMessage ByteResponse(HttpStatusCode statusCode, byte[] content, string contentType)
{
    var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(content) };
    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
    return response;
}

sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
