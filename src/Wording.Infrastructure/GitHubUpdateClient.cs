using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed class GitHubUpdateClient(HttpClient? http = null) : IApplicationUpdateClient
{
    public const string Repository = "ming0071/Wording";
    private static readonly HttpClient DefaultHttp = new() { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private readonly HttpClient client = http ?? DefaultHttp;

    public async Task<ApplicationRelease?> CheckAsync(Version current, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("Wording-Update/5.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("更新資訊超出大小限制。");
        return ParseRelease(json, current);
    }

    public static ApplicationRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$") ||
            !Version.TryParse(tag[1..], out var version)) throw new InvalidDataException("新版版本號無效。");
        if (version <= current) return null;
        var name = $"Wording-{version}-win-x64-Setup.exe";
        var assets = root.GetProperty("assets").EnumerateArray().Where(x => x.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("此版本尚未提供完整的 Windows x64 安裝檔。");
        var asset = assets[0];
        var expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        var expectedPage = $"https://github.com/{Repository}/releases/tag/{tag}";
        if (asset.GetProperty("browser_download_url").GetString() != expectedUrl ||
            root.GetProperty("html_url").GetString() != expectedPage ||
            asset.GetProperty("state").GetString() != "uploaded") throw new InvalidDataException("更新下載來源無效。");
        var digest = asset.GetProperty("digest").GetString() ?? "";
        var size = asset.GetProperty("size").GetInt64();
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || size is < 64 or > 1024L * 1024 * 1024)
            throw new InvalidDataException("安裝檔缺少有效的校驗資訊。");
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new(version, notes, new(expectedPage), new(expectedUrl), digest[7..].ToLowerInvariant(), size);
    }

    public async Task<DownloadedUpdate> DownloadAsync(ApplicationRelease release, string cacheDirectory,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        var expected = $"https://github.com/{Repository}/releases/download/v{release.Version}/Wording-{release.Version}-win-x64-Setup.exe";
        if (release.Installer.AbsoluteUri != expected || !Regex.IsMatch(release.Sha256, "^[a-f0-9]{64}$") ||
            release.Size is < 64 or > 1024L * 1024 * 1024) throw new InvalidDataException("更新資訊無效，請重新檢查版本。");
        var directory = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Wording-{release.Version}-win-x64-Setup.exe");
        var partial = path + ".partial";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, release.Installer);
            request.Headers.UserAgent.ParseAdd("Wording-Update/5.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                throw new InvalidDataException("下載大小與更新資訊不一致，請稍後重試。");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > release.Size) throw new InvalidDataException("下載超出預期大小。");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    progress?.Report(new(total, release.Size));
                }
                if (total != release.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安裝檔校驗失敗，未執行更新。請重新下載。");
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, path);
            return new(release, path);
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            if (File.Exists(path)) File.Delete(path);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }
}
