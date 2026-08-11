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
    Assert(item.Location == "중앙도서관 / 자료실", $"소장위치 형식 불일치: {item.Location}");
});

Run("지점별 보존서고 소장위치 형식", () =>
{
    foreach (var branch in new[] { "중앙도서관", "증평도서관", "의왕도서관" })
    {
        var html = $"""
            <div class="itemBranch">{branch}</div>
            <tr class="tbRecord1">
              <td>1</td><td>EM00000001</td><td>보존서고</td>
              <td>001 테57ㅈ</td><td>대출가능</td><td>-</td>
            </tr>
            """;
        var item = LibraryClient.ParseItems(html).Single();
        Assert(item.Location == $"{branch} / 보존서고", $"{branch} 소장위치 형식 불일치: {item.Location}");
    }

    const string prefixedHtml = """
        <div class="itemBranch">중앙도서관</div>
        <tr class="tbRecord1">
          <td>1</td><td>EM00000002</td><td>중앙도서관 / 보존서고</td>
          <td>001 테57ㅈ</td><td>대출가능</td><td>-</td>
        </tr>
        """;
    var prefixedItem = LibraryClient.ParseItems(prefixedHtml).Single();
    Assert(prefixedItem.Location == "중앙도서관 / 보존서고", $"지점명 중복 추가: {prefixedItem.Location}");
});

Run("TXT 등록번호 추출과 중복 제거", () =>
{
    var path = Path.Combine(artifacts, "input-sample.txt");
    File.WriteAllText(path, "등록번호\nEM00510342\nem00510342\n메모, WM00000001\n무시할 값", new UTF8Encoding(false));
    var values = RegistrationNumberReader.Read(path);
    Assert(values.SequenceEqual(["EM00510342", "WM00000001"]), string.Join(",", values));
});

Run("등록번호 입력 요약", () =>
{
    var summary = RegistrationNumberReader.ReadText(
        "등록번호\nEM00510342\nem00510342\n메모, WM00000001\n무시할 값");
    Assert(summary.Numbers.SequenceEqual(["EM00510342", "WM00000001"]), "등록번호 요약 결과 불일치");
    Assert(summary.ValuesScanned == 5, $"스캔 값 수 불일치: {summary.ValuesScanned}");
    Assert(summary.MatchesFound == 3, $"일치 수 불일치: {summary.MatchesFound}");
    Assert(summary.DuplicatesRemoved == 1, $"중복 제거 수 불일치: {summary.DuplicatesRemoved}");
});

Run("XLSX 쓰기/읽기 왕복", () =>
{
    var path = Path.Combine(artifacts, "roundtrip.xlsx");
    XlsxExporter.Write(path,
    [
        new BookResult { Sequence = 1, RegistrationNumber = "EM00510342", QueryState = "성공", BookState = "대출가능", Title = "인생의 역사", Author = "신형철", Publisher = "난다", PublicationYear = "2022", Isbn = "9791191859379", CatalogLastChanged = "20260806", DataSource = "테스트" },
        new BookResult { Sequence = 2, RegistrationNumber = "WM00000001", QueryState = "실패", Message = "테스트" }
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

Run("자동저장 결과 체크포인트 복원", () =>
{
    var checkedAt = new DateTime(2026, 8, 11, 14, 30, 0);
    var path = Path.Combine(artifacts, "checkpoint.xlsx");
    XlsxExporter.Write(path,
    [
        new BookResult
        {
            Sequence = 1,
            RegistrationNumber = "EM00510342",
            QueryState = "실시간 확인",
            BookState = "대출중",
            ReturnDue = "2026-08-20",
            Location = "중앙도서관",
            CallNumber = "811.7 신94ㅇ",
            Title = "인생의 역사",
            Author = "신형철",
            DataSource = "도서관 실시간 조회",
            CheckedAt = checkedAt
        },
        new BookResult
        {
            Sequence = 2,
            RegistrationNumber = "WM00000001",
            QueryState = "로컬 스냅샷",
            Message = "로컬 복원"
        },
        new BookResult
        {
            Sequence = 3,
            RegistrationNumber = "EM00000003",
            QueryState = "미확인",
            Message = "재조회 필요"
        },
        new BookResult
        {
            Sequence = 4,
            RegistrationNumber = "EM00000004",
            QueryState = "오류"
        },
        new BookResult
        {
            Sequence = 5,
            RegistrationNumber = "EM00000005",
            QueryState = "조회 중"
        },
        new BookResult
        {
            Sequence = 6,
            RegistrationNumber = "EM00000006",
            QueryState = "중지됨"
        },
        new BookResult
        {
            Sequence = 7,
            RegistrationNumber = "EM00000007",
            QueryState = "성공 "
        },
        new BookResult
        {
            Sequence = 8,
            RegistrationNumber = "EM00000008",
            QueryState = "임의상태",
            Message = "알 수 없는 이전 상태"
        }
    ]);

    var rows = ResultWorkbookReader.Read(path);
    Assert(rows.Count == 8, $"체크포인트 행 수 불일치: {rows.Count}");
    Assert(rows[0].RegistrationNumber == "EM00510342" && rows[0].QueryState == "성공", "체크포인트 성공 상태 복원 실패");
    Assert(rows[0].ReturnDue == "2026-08-20" && rows[0].Title == "인생의 역사", "체크포인트 상세정보 복원 실패");
    Assert(rows[0].CheckedAt == checkedAt, $"조회시각 복원 실패: {rows[0].CheckedAt}");
    Assert(rows[1].QueryState == "성공", "로컬 스냅샷 상태 복원 실패");
    Assert(rows[2].QueryState == "실패" && rows[2].Message == "재조회 필요", "미확인 상태 복원 실패");
    Assert(rows[3].QueryState == "실패", "오류 상태 복원 실패");
    Assert(rows[4].QueryState == "대기", "조회 중 상태 복원 실패");
    Assert(rows[5].QueryState == "대기", "중지됨 상태 복원 실패");
    Assert(rows[6].QueryState == "성공", "공백 포함 성공 상태 복원 실패");
    Assert(rows[7].QueryState == "실패", $"알 수 없는 상태가 네 상태 밖으로 노출됨: {rows[7].QueryState}");
});

Run("조회결과 네 가지 상태 분류", () =>
{
    var live = new LookupData(
        true, "대출중", "", "", "", "", "", "", "조회 완료",
        DataSource: "도서관 실시간 조회");
    var merged = live with { DataSource = "도서관 실시간 조회 + 로컬 전체목록" };
    var local = live with { DataSource = "로컬 전체목록" };
    var missing = LookupData.NotFound("미확인");

    Assert(LookupResultClassifier.Pending == "대기", "대기 상태 값 불일치");
    Assert(LookupResultClassifier.Running == "조회중", "조회중 상태 값 불일치");
    Assert(LookupResultClassifier.GetQueryState(live) == "성공", "실시간 성공 분류 실패");
    Assert(LookupResultClassifier.GetQueryState(merged) == "성공", "실시간+로컬 성공 분류 실패");
    Assert(LookupResultClassifier.GetQueryState(local) == "성공", "로컬 성공 분류 실패");
    Assert(LookupResultClassifier.GetQueryState(missing) == "실패", "실패 상태 분류 실패");

    Assert(!LookupResultClassifier.ShouldRetry(new BookResult
    {
        Sequence = 1,
        RegistrationNumber = "EM00510342",
        QueryState = LookupResultClassifier.Success
    }), "성공 행이 재조회 대상으로 분류됨");
    Assert(LookupResultClassifier.ShouldRetry(new BookResult
    {
        Sequence = 2,
        RegistrationNumber = "WM00000001",
        QueryState = LookupResultClassifier.Failed
    }), "실패 행이 재조회 대상이 아님");

    Assert(new BookResult
    {
        Sequence = 3,
        RegistrationNumber = "EM00000003",
        QueryState = LookupResultClassifier.Success
    }.IsSuccess, "성공 행 스타일 분류 실패");
});

Run("재조회 행 준비와 취소 후 재개", () =>
{
    var successful = new BookResult
    {
        Sequence = 1,
        RegistrationNumber = "EM00000001",
        QueryState = LookupResultClassifier.Success,
        Title = "보존할 성공 행"
    };
    var attempted = new BookResult
    {
        Sequence = 2,
        RegistrationNumber = "EM00000002",
        QueryState = LookupResultClassifier.Failed,
        Title = "초기화할 실패 행"
    };
    var untouched = new BookResult
    {
        Sequence = 3,
        RegistrationNumber = "EM00000003",
        QueryState = LookupResultClassifier.Failed,
        Title = "아직 시도하지 않은 행"
    };
    var rows = new[] { successful, attempted, untouched };

    Assert(LookupRunState.SelectRows(rows, retryOnly: true).SequenceEqual([attempted, untouched]), "재조회 대상 선택 실패");
    LookupRunState.PrepareRow(attempted);
    Assert(string.IsNullOrEmpty(attempted.Title), "시도 행 초기화 실패");
    Assert(untouched.Title == "아직 시도하지 않은 행", "미시도 행 정보가 지워짐");
    Assert(LookupRunState.ShouldResumePendingOnly(wasCanceled: true, rows), "취소 후 재개 모드가 유지되지 않음");
    Assert(!LookupRunState.ShouldResumePendingOnly(wasCanceled: false, rows), "완료 후 재개 모드가 남음");
});

Run("사용자 데이터 폴더 생성과 오래된 미리보기 정리", () =>
{
    var root = Path.Combine(artifacts, "app-data");
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    var paths = new AppDataPaths(root);
    paths.EnsureCreated();
    Assert(Directory.Exists(paths.ResultsDirectory), "결과 폴더 생성 실패");
    Assert(Directory.Exists(paths.PreviewsDirectory), "미리보기 폴더 생성 실패");
    Assert(Directory.Exists(paths.UpdatesDirectory), "업데이트 폴더 생성 실패");

    var oldPreview = Path.Combine(paths.PreviewsDirectory, "old.xlsx");
    var currentPreview = Path.Combine(paths.PreviewsDirectory, "current.xlsx");
    File.WriteAllText(oldPreview, "old");
    File.WriteAllText(currentPreview, "current");
    File.SetLastWriteTimeUtc(oldPreview, new DateTime(2026, 7, 1));
    File.SetLastWriteTimeUtc(currentPreview, new DateTime(2026, 8, 10));

    var removed = paths.CleanupPreviews(new DateTime(2026, 8, 11), TimeSpan.FromDays(7));
    Assert(removed == 1 && !File.Exists(oldPreview), "오래된 미리보기 정리 실패");
    Assert(File.Exists(currentPreview), "최근 미리보기가 잘못 삭제됨");
});

Run("진단 정보에서 등록번호 제외", () =>
{
    var paths = new AppDataPaths(Path.Combine(artifacts, "diagnostics"));
    var reportPath = DiagnosticReportWriter.Write(
        paths,
        "1.3.0",
        @"C:\input\도서목록.xlsx",
        [
            new BookResult
            {
                Sequence = 1,
                RegistrationNumber = "EM00510342",
                QueryState = LookupResultClassifier.Success,
                Title = "민감 서명",
                Author = "민감 저자",
                Location = "민감 위치",
                CallNumber = "민감 청구기호",
                DetailUrl = "https://example.invalid/private",
                Message = "민감 메시지"
            }
        ]);
    var report = File.ReadAllText(reportPath);
    Assert(report.Contains("버전: 1.3.0"), "진단 버전 누락");
    Assert(report.Contains("입력 표시명: 도서목록.xlsx"), "진단 입력 표시명 누락");
    Assert(!report.Contains("EM00510342"), "진단 정보에 등록번호가 노출됨");
    Assert(!report.Contains("민감"), "진단 정보에 도서 상세정보가 노출됨");
    Assert(!report.Contains("example.invalid"), "진단 정보에 상세 URL이 노출됨");
});

Run("비정상 XLSX 압축률 차단", () =>
{
    var path = Path.Combine(artifacts, "zip-bomb.xlsx");
    if (File.Exists(path)) File.Delete(path);
    using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(new string('A', 2 * 1024 * 1024));
    }

    var rejected = false;
    try
    {
        RegistrationNumberReader.Read(path);
    }
    catch (InvalidDataException ex) when (ex.Message.Contains("압축률", StringComparison.Ordinal))
    {
        rejected = true;
    }
    Assert(rejected, "비정상 XLSX 압축률이 차단되지 않음");
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

Run("처리 속도 표본과 이동 평균", () =>
{
    var tracker = new ProcessingSpeedTracker(capacity: 3, smoothingWindow: 2);
    Assert(Math.Abs(tracker.AddSample(1, TimeSpan.FromSeconds(2)) - 30) < 0.001, "첫 처리 속도 계산 오류");
    Assert(Math.Abs(tracker.AddSample(2, TimeSpan.FromSeconds(3)) - 45) < 0.001, "이동 평균 계산 오류");
    tracker.AddSample(3, TimeSpan.FromSeconds(5));
    tracker.AddSample(4, TimeSpan.FromSeconds(6));
    Assert(tracker.Samples.Count == 3, "그래프 표본 용량 제한 오류");
    tracker.Reset();
    Assert(tracker.Samples.Count == 0 && tracker.CurrentItemsPerMinute == 0, "속도 추적 초기화 오류");
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
