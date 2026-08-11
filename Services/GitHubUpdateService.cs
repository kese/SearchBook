using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SearchBook.Services;

public sealed class GitHubUpdateService : IDisposable
{
    public const string Repository = "StandardChartered/SearchBook";
    public const string ReleasesUrl = "https://github.com/StandardChartered/SearchBook/releases/latest";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/StandardChartered/SearchBook/releases/latest";
    private readonly HttpClient _httpClient;
    private readonly bool _hasToken;

    public GitHubUpdateService(string? token = null)
        : this(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }, token)
    {
    }

    public GitHubUpdateService(HttpMessageHandler handler, string? token = null)
    {
        _hasToken = !string.IsNullOrWhiteSpace(token);
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SearchBook-Updater/1.3.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_hasToken)
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            var message = _hasToken
                ? "GitHub 릴리즈를 읽을 권한이 없거나 최신 릴리즈가 없습니다."
                : "private 저장소 릴리즈를 확인하려면 SEARCHBOOK_GITHUB_TOKEN이 필요합니다.";
            return new UpdateCheckResult(
                _hasToken ? UpdateCheckStatus.Failed : UpdateCheckStatus.AuthenticationRequired,
                currentVersion,
                null,
                "",
                ReleasesUrl,
                null,
                message);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                currentVersion,
                null,
                "",
                ReleasesUrl,
                null,
                $"GitHub 응답 오류: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = GetString(root, "tag_name");
        var releaseUrl = GetString(root, "html_url");
        if (string.IsNullOrWhiteSpace(releaseUrl)) releaseUrl = ReleasesUrl;
        if (!TryParseVersion(tagName, out var latestVersion))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                currentVersion,
                null,
                tagName,
                releaseUrl,
                null,
                $"릴리즈 태그에서 버전을 읽을 수 없습니다: {tagName}");
        }

        var assets = ReadAssets(root);
        var preferredAsset = assets.FirstOrDefault(asset =>
                                 asset.Name.Equals("SearchBook-self-contained-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                             ?? assets.FirstOrDefault(asset =>
                                 asset.Name.Equals("SearchBook-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        var isNewer = Normalize(latestVersion).CompareTo(Normalize(currentVersion)) > 0;

        if (isNewer && preferredAsset is null)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NoCompatibleAsset,
                currentVersion,
                latestVersion,
                tagName,
                releaseUrl,
                null,
                "최신 릴리즈에 호환되는 Windows ZIP이 없습니다.");
        }

        return new UpdateCheckResult(
            isNewer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
            currentVersion,
            latestVersion,
            tagName,
            releaseUrl,
            preferredAsset,
            isNewer ? "새 업데이트를 사용할 수 있습니다." : "현재 최신 버전을 사용 중입니다.");
    }

    public async Task<string> DownloadAsync(
        GitHubReleaseAsset asset,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var safeAssetName = Path.GetFileName(asset.Name);
        var destinationFileName = Path.GetFileName(destinationPath);
        if (!safeAssetName.Equals(asset.Name, StringComparison.Ordinal) ||
            !destinationFileName.Equals(safeAssetName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("다운로드 파일명이 릴리즈 자산명과 일치하지 않습니다.");
        if (!TryGetSha256(asset.Digest, out var expectedHash))
            throw new InvalidDataException("릴리즈 자산에 검증 가능한 SHA-256 정보가 없습니다.");

        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporaryPath = fullDestination + ".download";
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.ApiUrl);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81_920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var fileInfo = new FileInfo(temporaryPath);
            if (asset.Size > 0 && fileInfo.Length != asset.Size)
                throw new InvalidDataException($"다운로드 크기가 다릅니다. 예상 {asset.Size:N0}바이트, 실제 {fileInfo.Length:N0}바이트");

            string actualHash;
            await using (var file = File.OpenRead(temporaryPath))
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("다운로드한 업데이트의 SHA-256 검증에 실패했습니다.");

            File.Move(temporaryPath, fullDestination, overwrite: true);
            return fullDestination;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static IReadOnlyList<GitHubReleaseAsset> ReadAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
            return [];

        var assets = new List<GitHubReleaseAsset>();
        foreach (var asset in assetsElement.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var apiUrl = GetString(asset, "url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(apiUrl)) continue;
            assets.Add(new GitHubReleaseAsset(
                name,
                apiUrl,
                GetString(asset, "browser_download_url"),
                GetString(asset, "digest"),
                asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var value) ? value : 0));
        }
        return assets;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";

    private static bool TryParseVersion(string tagName, out Version version)
    {
        var value = tagName.Trim().TrimStart('v', 'V');
        var suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0) value = value[..suffix];
        return Version.TryParse(value, out version!);
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private static bool TryGetSha256(string digest, out string hash)
    {
        const string prefix = "sha256:";
        if (digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && digest.Length == prefix.Length + 64)
        {
            hash = digest[prefix.Length..];
            return true;
        }
        hash = "";
        return false;
    }
}

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    AuthenticationRequired,
    NoCompatibleAsset,
    Failed
}

public sealed record GitHubReleaseAsset(string Name, string ApiUrl, string BrowserDownloadUrl, string Digest, long Size);

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version CurrentVersion,
    Version? LatestVersion,
    string TagName,
    string ReleaseUrl,
    GitHubReleaseAsset? Asset,
    string Message);
