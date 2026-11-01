using System.Diagnostics;
using System.IO;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop.ViewModels;

public sealed class UpdateViewModel : PageViewModel
{
    private readonly IApplicationUpdateClient client;
    private readonly IUpdateInstaller installer;
    private readonly IBackupService backup;
    private readonly AppSettings settings;
    private readonly Action saveSettings;
    private readonly Func<string?> restartBlockReason;
    private readonly Action exitApplication;
    private readonly string dataDirectory;
    private readonly Version current;
    private ApplicationRelease? release;
    private DownloadedUpdate? downloaded;
    private UpdatePhase phase;
    private bool dismissed;
    private double progress;
    private string status = "開啟程式時會自動檢查新版，也可在這裡手動檢查。";
    public UpdatePhase Phase { get => phase; private set { SetProperty(ref phase, value); Refresh(); } }
    public string CurrentVersion => current.ToString();
    public string LatestVersion => release?.Version.ToString() ?? "";
    public string ReleaseNotes => release?.Notes.Length > 6000 ? release.Notes[..6000] + "…" : release?.Notes ?? "";
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public double Progress { get => progress; private set => SetProperty(ref progress, value); }
    public bool IsDownloading => Phase == UpdatePhase.Downloading;
    public bool CanUseApplication => Phase != UpdatePhase.Installing;
    public bool HasUpdate => release is not null;
    public bool HasLaunchedInstaller { get; private set; }
    public bool ShowBanner => !dismissed && (HasUpdate || IsDownloading || Phase == UpdatePhase.Installing);
    public string InstallButtonText => downloaded is not null ? "安裝並重新開啟" : "下載並更新";
    private bool Working => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;
    public AsyncCommand CheckCommand { get; }
    public AsyncCommand InstallCommand { get; }
    public AsyncCommand SkipCommand { get; }
    public RelayCommand LaterCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ViewReleaseCommand { get; }

    public UpdateViewModel(IApplicationUpdateClient client, IUpdateInstaller installer, IBackupService backup,
        AppSettings settings, Version current, string dataDirectory, Action saveSettings,
        Func<string?> restartBlockReason, Action exitApplication)
    {
        this.client = client; this.installer = installer; this.backup = backup; this.settings = settings;
        this.current = current; this.dataDirectory = dataDirectory; this.saveSettings = saveSettings;
        this.restartBlockReason = restartBlockReason; this.exitApplication = exitApplication;
        CheckCommand = Command(token => CheckAsync(true, token), () => !Working);
        InstallCommand = Command(InstallAsync, () => HasUpdate && !Working);
        SkipCommand = Command(_ =>
        {
            settings.SkippedUpdateVersion = LatestVersion;
            saveSettings();
            dismissed = true; Refresh();
            return Task.CompletedTask;
        }, () => HasUpdate && !Working);
        LaterCommand = new(_ => { dismissed = true; Refresh(); }, _ => !Working);
        CancelCommand = new(_ => InstallCommand.Cancel(), _ => IsDownloading);
        ViewReleaseCommand = new(_ => Process.Start(new ProcessStartInfo(release!.Page.AbsoluteUri) { UseShellExecute = true }), _ => HasUpdate);
    }

    public async Task CheckInBackgroundAsync(CancellationToken token = default)
    {
        if (Working || settings.LastUpdateCheckUtc is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(6)) return;
        await CheckAsync(false, token);
    }

    private async Task CheckAsync(bool manual, CancellationToken token)
    {
        Error = "";
        Notice = "";
        Phase = UpdatePhase.Checking;
        Status = "正在檢查新版本…";
        try
        {
            var available = await client.CheckAsync(current, token);
            settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            saveSettings();
            if (!manual && available?.Version.ToString() == settings.SkippedUpdateVersion) available = null;
            if (release?.Version != available?.Version || release?.Sha256 != available?.Sha256) downloaded = null;
            release = available;
            dismissed = false;
            Phase = available is null ? UpdatePhase.Current : UpdatePhase.Available;
            Status = available is null ? "目前沒有比此版本更新的正式發行版。" : $"發現 Wording {LatestVersion}，可以下載並更新。";
        }
        catch (Exception exception)
        {
            Phase = release is null ? UpdatePhase.Idle : UpdatePhase.Available;
            Status = "暫時無法檢查更新，離線學習仍可使用。";
            if (manual && exception is not OperationCanceledException) Error = Status;
            if (token.IsCancellationRequested) return;
            AppDiagnostics.Write($"Update check unavailable: {exception.GetType().Name}");
        }
    }

    private async Task InstallAsync(CancellationToken token)
    {
        var launched = false;
        try
        {
            if (downloaded is null)
            {
                Phase = UpdatePhase.Downloading;
                Status = $"正在下載 Wording {LatestVersion}…";
                Progress = 0;
                downloaded = await client.DownloadAsync(release!, Path.Combine(dataDirectory, "Updates"),
                    new Progress<UpdateProgress>(value => Progress = value.Percent), token);
            }
            Phase = UpdatePhase.Ready;
            if (restartBlockReason() is { } reason)
            {
                Notice = reason;
                Status = "新版已下載。完成目前操作後，按「安裝並重新開啟」。";
                return;
            }
            token.ThrowIfCancellationRequested();
            Phase = UpdatePhase.Installing;
            HasLaunchedInstaller = false;
            Status = "正在備份學習資料，接著安裝並重新開啟…";
            var backupDirectory = Path.Combine(dataDirectory, "Backups");
            Directory.CreateDirectory(backupDirectory);
            await backup.CreateBackupAsync(Path.Combine(backupDirectory,
                $"before-update-{current}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip"), token);
            await installer.LaunchAsync(downloaded, dataDirectory, token);
            launched = true;
            HasLaunchedInstaller = true;
            exitApplication();
        }
        catch (Exception exception)
        {
            if (exception is InvalidDataException or FileNotFoundException) downloaded = null;
            Status = "更新未完成，原版本仍可使用。請稍後重試。";
            throw;
        }
        finally
        {
            if (!launched) Phase = downloaded is null ? UpdatePhase.Available : UpdatePhase.Ready;
        }
    }

    private void Refresh()
    {
        foreach (var name in new[] { nameof(LatestVersion), nameof(ReleaseNotes), nameof(IsDownloading),
            nameof(CanUseApplication), nameof(HasUpdate), nameof(ShowBanner), nameof(InstallButtonText) }) OnPropertyChanged(name);
        CheckCommand?.NotifyCanExecuteChanged(); InstallCommand?.NotifyCanExecuteChanged(); SkipCommand?.NotifyCanExecuteChanged();
        LaterCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); ViewReleaseCommand?.NotifyCanExecuteChanged();
    }
}
