using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using SearchBook.Models;

namespace SearchBook.Services;

public sealed partial class LibraryClient : IDisposable
{
    private const string BaseUrl = "https://chains.ut.ac.kr";
    private readonly HttpClient _httpClient;
    private readonly LookupSettings _settings;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public LibraryClient(LookupSettings settings)
    {
        _settings = settings;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SearchBook/1.0 (library inventory helper; single-request mode)");
        _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("ko-KR"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
    }

    public async Task<LookupData> LookupAsync(string registrationNumber, CancellationToken cancellationToken)
    {
        var encodedQuery = Uri.EscapeDataString($"IDID:{registrationNumber}");
        var encodedNumber = Uri.EscapeDataString(registrationNumber);
        var searchUrl = $"{BaseUrl}/search/Search.Result.ax?sid=1&q={encodedQuery}&mf=true&qt={encodedQuery}&qf={encodedNumber}&pageSize=10";
        var searchHtml = await GetTextWithRetryAsync(searchUrl, cancellationToken);

        var cids = ParseCids(searchHtml).Distinct(StringComparer.Ordinal).ToList();
        if (cids.Count == 0)
            return LookupData.NotFound("검색 결과에서 일치하는 서지를 찾지 못했습니다.");

        foreach (var cid in cids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (title, bibliographicInfo) = ParseBibliographic(searchHtml, cid);
            var branches = ParseBranches(searchHtml, cid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (branches.Count == 0) branches.Add("01");

            foreach (var branch in branches)
            {
                var itemUrl = $"{BaseUrl}/common/AjaxHtmlGenerator.ax?targetUrl=search%2FItemDetail.axa&cid={Uri.EscapeDataString(cid)}&sid=1&branchCode={Uri.EscapeDataString(branch)}";
                var itemHtml = await GetTextWithRetryAsync(itemUrl, cancellationToken);
                var matchingItem = ParseItems(itemHtml).FirstOrDefault(item =>
                    string.Equals(item.RegistrationNumber, registrationNumber, StringComparison.OrdinalIgnoreCase));
                if (matchingItem is null) continue;

                return new LookupData(
                    true,
                    matchingItem.BookState,
                    matchingItem.ReturnDue,
                    matchingItem.Location,
                    matchingItem.CallNumber,
                    title,
                    bibliographicInfo,
                    $"{BaseUrl}/search/DetailView.ax?sid=1&cid={cid}",
                    "조회 완료",
                    DataSource: "도서관 실시간 조회");
            }
        }

        return LookupData.NotFound("서지는 찾았지만 해당 등록번호의 소장정보 행을 찾지 못했습니다.");
    }

    public static IReadOnlyList<string> ParseCids(string html) =>
        CidRegex().Matches(html).Select(match => match.Groups[1].Value).ToList();

    public static IReadOnlyList<string> ParseBranches(string html, string cid)
    {
        var pattern = $"showBriefItem\\(\\s*{Regex.Escape(cid)}\\s*,\\s*['\\\"](?<branch>[^'\\\"]+)['\\\"]\\s*\\)";
        return Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["branch"].Value).ToList();
    }

    public static (string Title, string BibliographicInfo) ParseBibliographic(string html, string cid)
    {
        var cidPattern = Regex.Escape(cid);
        var titleMatch = Regex.Match(html,
            $"goDetail\\(\\s*{cidPattern}\\s*\\)\\s*;?[^>]*class=['\\\"]title['\\\"][^>]*>(?<title>[\\s\\S]*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var title = titleMatch.Success ? CleanHtml(titleMatch.Groups["title"].Value) : "";

        var metaMatch = Regex.Match(html,
            $"goDetail\\(\\s*{cidPattern}\\s*,\\s*true\\s*\\)\\s*;?[\\s\\S]*?</a>\\s*/\\s*(?<meta>[\\s\\S]*?)(?:<div|<br|</li>)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var meta = metaMatch.Success ? CleanHtml(metaMatch.Groups["meta"].Value) : "";
        return (title, meta);
    }

    public static IReadOnlyList<ItemData> ParseItems(string html)
    {
        var result = new List<ItemData>();
        foreach (Match row in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(row.Groups[1].Value)
                .Select(match => CleanHtml(match.Groups[1].Value)).ToList();
            if (cells.Count < 6) continue;
            var registrationNumber = cells[1].Trim().ToUpperInvariant();
            if (!RegistrationRegex().IsMatch(registrationNumber)) continue;
            result.Add(new ItemData(
                registrationNumber,
                cells.ElementAtOrDefault(2) ?? "",
                cells.ElementAtOrDefault(3) ?? "",
                cells.ElementAtOrDefault(4) ?? "",
                NormalizeEmpty(cells.ElementAtOrDefault(5) ?? "")));
        }
        return result;
    }

    private async Task<string> GetTextWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= _settings.MaxRetries; attempt++)
        {
            try
            {
                await ThrottleAsync(cancellationToken);
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    throw new HttpRequestException($"도서관 서버 응답: {(int)response.StatusCode} {response.ReasonPhrase}");
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return Decode(bytes, response.Content.Headers.ContentType?.CharSet);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                if (attempt >= _settings.MaxRetries) break;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cancellationToken);
            }
        }
        throw new HttpRequestException("도서관 서버 요청이 재시도 후에도 실패했습니다.", lastException);
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var wait = TimeSpan.FromMilliseconds(_settings.DelayMilliseconds) - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"', '\'')).GetString(bytes); }
            catch (ArgumentException) { }
        }

        var asciiPrefix = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var match = CharsetRegex().Match(asciiPrefix);
        if (match.Success)
        {
            try { return Encoding.GetEncoding(match.Groups[1].Value).GetString(bytes); }
            catch (ArgumentException) { }
        }

        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(bytes); }
    }

    private static string CleanHtml(string value)
    {
        var withoutTags = TagsRegex().Replace(value, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags).Replace('\u00A0', ' ');
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string NormalizeEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) || value == "-" ? "" : value;

    public void Dispose()
    {
        _requestGate.Dispose();
        _httpClient.Dispose();
    }

    [GeneratedRegex(@"(?:javascript:search\.)?goDetail\(\s*(\d+)\s*(?:,\s*(?:true|false))?\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CidRegex();
    [GeneratedRegex("""<tr\b[^>]*class=['"][^'"]*tbRecord[^'"]*['"][^>]*>([\s\S]*?)</tr>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RowRegex();
    [GeneratedRegex(@"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CellRegex();
    [GeneratedRegex(@"^(?:EM|WM)[A-Z0-9-]{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegistrationRegex();
    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagsRegex();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex("""charset\s*=\s*['"]?([A-Za-z0-9._-]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CharsetRegex();
}

public sealed record ItemData(
    string RegistrationNumber,
    string Location,
    string CallNumber,
    string BookState,
    string ReturnDue);

public sealed record LookupData(
    bool Success,
    string BookState,
    string ReturnDue,
    string Location,
    string CallNumber,
    string Title,
    string BibliographicInfo,
    string DetailUrl,
    string Message,
    string Author = "",
    string Publisher = "",
    string PublicationYear = "",
    string Isbn = "",
    string CatalogLastChanged = "",
    string DataSource = "")
{
    public static LookupData NotFound(string message) => new(false, "", "", "", "", "", "", "", message);
}
