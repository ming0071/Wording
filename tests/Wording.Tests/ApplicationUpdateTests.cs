using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class ApplicationUpdateTests
{
    private static readonly byte[] InstallerBytes = [77, 90, .. Enumerable.Repeat((byte)42, 126)];
    private static string Checksum => Convert.ToHexString(SHA256.HashData(InstallerBytes)).ToLowerInvariant();
    private static string Metadata(string version = "5.0.10", string? digest = null, string? url = null, bool prerelease = false) =>
        JsonSerializer.Serialize(new { draft = false, prerelease, tag_name = "v" + version,
            html_url = $"https://github.com/ming0071/Wording/releases/tag/v{version}", body = "新版內容",
            assets = new[] { new { name = $"Wording-{version}-win-x64-Setup.exe", state = "uploaded", size = InstallerBytes.Length,
                digest = digest ?? "sha256:" + Checksum,
                browser_download_url = url ?? $"https://github.com/ming0071/Wording/releases/download/v{version}/Wording-{version}-win-x64-Setup.exe" } } });

    [Fact]
    public void VersionComparisonIsNumericAndNeverOffersOldOrPrereleaseVersions()
    {
        Assert.Equal(new Version(5, 0, 10), GitHubUpdateClient.ParseRelease(Metadata(), new(5, 0, 9))!.Version);
        Assert.Null(GitHubUpdateClient.ParseRelease(Metadata(), new(5, 0, 10)));
        Assert.Null(GitHubUpdateClient.ParseRelease(Metadata("5.0.2"), new(5, 0, 9)));
        Assert.Null(GitHubUpdateClient.ParseRelease(Metadata(prerelease: true), new(4, 0, 0)));
    }

    [Theory]
    [InlineData("bad-digest", null)]
    [InlineData(null, "https://example.com/installer.exe")]
    [InlineData(null, "https://github.com/another/repo/releases/download/v5.0.10/Wording-5.0.10-win-x64-Setup.exe")]
    public void IncompleteChecksumsOrUnexpectedDownloadOriginsAreRejected(string? digest, string? url) =>
        Assert.Throws<InvalidDataException>(() => GitHubUpdateClient.ParseRelease(Metadata(digest: digest, url: url), new(5, 0, 0)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadChecksBytesAndCleansPartialFilesOnMismatch(bool tampered)
    {
        using var data = new StudyTestData();
        var release = GitHubUpdateClient.ParseRelease(Metadata(), new(5, 0, 0))!;
        var bytes = InstallerBytes.ToArray();
        if (tampered) bytes[^1]++;
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var client = new GitHubUpdateClient(http);
        if (tampered)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, data.DirectoryPath));
            Assert.Empty(Directory.GetFiles(data.DirectoryPath, "*", SearchOption.AllDirectories));
        }
        else
        {
            var downloaded = await client.DownloadAsync(release, data.DirectoryPath);
            Assert.Equal(InstallerBytes, await File.ReadAllBytesAsync(downloaded.InstallerPath));
            Assert.DoesNotContain(Directory.GetFiles(data.DirectoryPath, "*", SearchOption.AllDirectories), x => x.EndsWith(".partial"));
        }
    }

    [Fact]
    public async Task CancelledDownloadDoesNotLeaveExecutableOrPartialFiles()
    {
        using var data = new StudyTestData();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(InstallerBytes) }));
        var client = new GitHubUpdateClient(http);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(
            GitHubUpdateClient.ParseRelease(Metadata(), new(5, 0, 0))!, data.DirectoryPath, token: cancel.Token));
        Assert.Empty(Directory.GetFiles(data.DirectoryPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MissingReleaseIsNormalAndPublicChecksNeedNoCredentials()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/ming0071/Wording/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.NotFound);
        }));
        Assert.Null(await new GitHubUpdateClient(http).CheckAsync(new(5, 0, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateWaitsForWorkAndBacksUpBeforeInstallerAndExit(bool installerFails)
    {
        using var data = new StudyTestData();
        var events = new List<string>();
        var client = new FakeClient();
        var installer = new FakeInstaller(events) { Fails = installerFails };
        var blocked = true;
        var vm = new UpdateViewModel(client, installer, new FakeBackup(events), new(), new(5, 0, 0),
            data.DirectoryPath, () => { }, () => blocked ? "先完成練習" : null, () => events.Add("exit"));
        await vm.CheckCommand.ExecuteAsync();
        Assert.True(vm.ShowBanner);
        await vm.InstallCommand.ExecuteAsync();
        Assert.Equal(UpdatePhase.Ready, vm.Phase);
        Assert.Contains("先完成練習", vm.Notice);
        Assert.Empty(events);
        Assert.Equal(1, client.Downloads);
        blocked = false;
        await vm.InstallCommand.ExecuteAsync();
        Assert.Equal(1, client.Downloads);
        Assert.Equal(installerFails ? new[] { "backup", "install" } : ["backup", "install", "exit"], events);
        if (installerFails) { Assert.Equal(UpdatePhase.Ready, vm.Phase); Assert.True(vm.CanUseApplication); Assert.Contains("fixture", vm.Error); }
    }

    [Fact]
    public async Task InvalidCachedInstallerIsDownloadedAgainOnRetry()
    {
        using var data = new StudyTestData();
        var client = new FakeClient();
        var events = new List<string>();
        var installer = new FakeInstaller(events) { InvalidBytes = true };
        var vm = new UpdateViewModel(client, installer, new FakeBackup(events), new(), new(5, 0, 0), data.DirectoryPath,
            () => { }, () => null, () => events.Add("exit"));
        await vm.CheckCommand.ExecuteAsync();
        await vm.InstallCommand.ExecuteAsync();
        Assert.Equal(UpdatePhase.Available, vm.Phase);
        Assert.DoesNotContain("exit", events);
        installer.InvalidBytes = false;
        await vm.InstallCommand.ExecuteAsync();
        Assert.Equal(2, client.Downloads);
        Assert.Equal("exit", events.Last());
    }

    [Fact]
    public async Task AutomaticChecksRespectSkipAndManualCheckCanShowSkippedVersionAgain()
    {
        using var data = new StudyTestData();
        var client = new FakeClient();
        var settings = new AppSettings();
        var vm = new UpdateViewModel(client, new FakeInstaller([]), new FakeBackup([]), settings, new(5, 0, 0),
            data.DirectoryPath, () => settings.Save(data.DirectoryPath), () => null, () => { });
        await vm.CheckCommand.ExecuteAsync();
        await vm.SkipCommand.ExecuteAsync();
        Assert.False(vm.ShowBanner);
        Assert.Equal("5.0.10", AppSettings.Load(data.DirectoryPath).SkippedUpdateVersion);
        settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow.AddDays(-1);
        var restarted = new UpdateViewModel(client, new FakeInstaller([]), new FakeBackup([]), settings, new(5, 0, 0),
            data.DirectoryPath, () => { }, () => null, () => { });
        await restarted.CheckInBackgroundAsync();
        Assert.False(restarted.ShowBanner);
        await restarted.CheckCommand.ExecuteAsync();
        Assert.True(restarted.ShowBanner);
    }

    [Fact]
    public async Task OfflineAutomaticCheckDoesNotInterruptLearning()
    {
        using var data = new StudyTestData();
        var vm = new UpdateViewModel(new FakeClient { Offline = true }, new FakeInstaller([]), new FakeBackup([]), new(),
            new(5, 0, 0), data.DirectoryPath, () => { }, () => null, () => throw new Exception("must not exit"));
        await vm.CheckInBackgroundAsync();
        Assert.False(vm.ShowBanner); Assert.Equal("", vm.Error); Assert.True(vm.CanUseApplication);
        await vm.CheckCommand.ExecuteAsync();
        Assert.Contains("離線學習仍可使用", vm.Error);
    }

    [Fact]
    public async Task SavingOtherSettingsPreservesSkippedVersionAndCheckTime()
    {
        using var data = new StudyTestData();
        var settings = new AppSettings { SkippedUpdateVersion = "5.1.0", LastUpdateCheckUtc = DateTimeOffset.UtcNow };
        var vm = new SettingsViewModel(new FakeBackup([]), new PracticeFixtures.Generator(), new PracticeFixtures.Speech(),
            settings, new(), data.DirectoryPath, () => { });
        vm.DailyNewLimit = "55";
        await vm.SaveCommand.ExecuteAsync();
        Assert.Equal("", vm.Error);
        var saved = AppSettings.Load(data.DirectoryPath);
        Assert.Equal(settings.SkippedUpdateVersion, saved.SkippedUpdateVersion);
        Assert.Equal(settings.LastUpdateCheckUtc, saved.LastUpdateCheckUtc);
    }

    [Fact]
    public void InstallerArgumentsPreserveUnicodeAndCustomDataPathWithoutShellCommands()
    {
        var path = Path.Combine(Path.GetTempPath(), "自己的 詞庫");
        var args = UpdateInstaller.BuildArguments(123, path);
        Assert.Contains("/WORDINGPARENTPID=123", args);
        Assert.Contains("/WORDINGDATADIR=" + Path.GetFullPath(path), args);
        Assert.Contains("/NORESTART", args); Assert.Contains("/NOCLOSEAPPLICATIONS", args);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(action(request)); }
    }
    private sealed class FakeClient : IApplicationUpdateClient
    {
        public int Downloads; public bool Offline;
        public Task<ApplicationRelease?> CheckAsync(Version current, CancellationToken token = default) => Offline
            ? throw new HttpRequestException("offline") : Task.FromResult(GitHubUpdateClient.ParseRelease(Metadata(), current));
        public Task<DownloadedUpdate> DownloadAsync(ApplicationRelease release, string cache, IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
        { Downloads++; return Task.FromResult(new DownloadedUpdate(release, Path.Combine(cache, "fixture.exe"))); }
    }
    private sealed class FakeInstaller(List<string> events) : IUpdateInstaller
    {
        public bool Fails;
        public bool InvalidBytes;
        public Task LaunchAsync(DownloadedUpdate update, string directory, CancellationToken token = default)
        { events.Add("install"); if (InvalidBytes) throw new InvalidDataException("fixture bytes changed"); if (Fails) throw new IOException("fixture install failed"); return Task.CompletedTask; }
    }
    private sealed class FakeBackup(List<string> events) : IBackupService
    {
        public Task CreateBackupAsync(string path, CancellationToken token = default) { events.Add("backup"); return Task.CompletedTask; }
        public Task RestoreBackupAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
