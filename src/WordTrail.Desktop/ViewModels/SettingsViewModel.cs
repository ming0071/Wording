using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WordTrail.Core;
using WordTrail.Infrastructure;

namespace WordTrail.Desktop.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly IBackupService backup;
    private readonly IContentGenerator generator;
    private readonly AppSettings settings;
    private readonly AiSettings aiSettings;
    private readonly Action restored;
    private string dailyNewLimit;
    private string dailyGenerationLimit;
    private string codexPath;
    private string codexModel;
    public string DataDirectory { get; }
    public string SpeechStatus { get; }
    public string DailyNewLimit { get => dailyNewLimit; set => SetProperty(ref dailyNewLimit, value); }
    public string DailyGenerationLimit { get => dailyGenerationLimit; set => SetProperty(ref dailyGenerationLimit, value); }
    public string CodexPath { get => codexPath; set => SetProperty(ref codexPath, value); }
    public string CodexModel { get => codexModel; set => SetProperty(ref codexModel, value); }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CheckAiCommand { get; }
    public AsyncCommand BackupCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public SettingsViewModel(IBackupService backup, IContentGenerator generator, IPronunciationService speech,
        AppSettings settings, AiSettings aiSettings, string dataDirectory, Action restored)
    {
        this.backup = backup;
        this.generator = generator;
        this.settings = settings;
        this.aiSettings = aiSettings;
        this.restored = restored;
        DataDirectory = dataDirectory;
        SpeechStatus = speech.Status;
        dailyNewLimit = settings.DailyNewLimit.ToString();
        dailyGenerationLimit = settings.DailyGenerationLimit.ToString();
        codexPath = settings.CodexExecutablePath;
        codexModel = settings.CodexModel;
        SaveCommand = Command(SaveAsync);
        CheckAiCommand = Command(async token => Notice = await generator.CheckAvailabilityAsync(token));
        BackupCommand = Command(BackupAsync);
        RestoreCommand = Command(RestoreAsync);
        OpenFolderCommand = new(_ => OpenFolder());
    }

    private Task SaveAsync(CancellationToken token)
    {
        if (!int.TryParse(DailyNewLimit, out var newLimit) || newLimit is < 0 or > 10)
            throw new InvalidOperationException("每日新詞上限請填入 0–10 的整數。");
        if (!int.TryParse(DailyGenerationLimit, out var generationLimit) || generationLimit is < 0 or > 50)
            throw new InvalidOperationException("每日 AI 次數請填入 0–50 的整數；0 代表停用生成。");
        if (string.IsNullOrWhiteSpace(CodexPath)) throw new InvalidOperationException("請填入 Codex 執行檔路徑或 codex。");
        settings.DailyNewLimit = newLimit;
        settings.CodexExecutablePath = CodexPath.Trim();
        settings.CodexModel = CodexModel.Trim();
        settings.DailyGenerationLimit = generationLimit;
        settings.Save(DataDirectory);
        aiSettings.ExecutablePath = settings.CodexExecutablePath;
        aiSettings.Model = settings.CodexModel;
        aiSettings.DailyGenerationLimit = generationLimit;
        Notice = "設定已保存。新詞上限從下一次取卡開始套用。";
        return Task.CompletedTask;
    }

    private async Task BackupAsync(CancellationToken token)
    {
        var backupDirectory = Path.Combine(DataDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var dialog = new SaveFileDialog
        {
            Title = "備份單字庫", Filter = "WordTrail 備份 (*.zip)|*.zip", DefaultExt = ".zip",
            FileName = $"WordTrail-{DateTime.Now:yyyyMMdd-HHmmss}.zip", InitialDirectory = backupDirectory
        };
        if (dialog.ShowDialog() != true) return;
        await backup.CreateBackupAsync(dialog.FileName, token);
        Notice = $"備份已完成：{dialog.FileName}";
    }

    private async Task RestoreAsync(CancellationToken token)
    {
        var dialog = new OpenFileDialog { Title = "選擇要還原的 WordTrail 備份", Filter = "WordTrail 備份 (*.zip)|*.zip" };
        if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show($"將用以下備份替換目前整個單字庫與複習紀錄：\n{dialog.FileName}\n\n程式會先保存現有資料的復原備份。確定還原？",
            "還原整個單字庫", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await backup.RestoreBackupAsync(dialog.FileName, token);
        restored();
        Notice = "還原完成。回到今日學習或單字庫即可查看。";
    }

    private void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true }); }
        catch (Exception exception) { Error = $"無法開啟資料目錄：{exception.Message}"; }
    }
}
