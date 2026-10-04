using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Numerics;
using System.Windows;
using Microsoft.Win32;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop.ViewModels;

public sealed record CodexModelChoice(string Model, string Label, string Description, bool IsAvailable);
public sealed record CodexSpeedChoice(string Id, string Label, string Description, bool IsAvailable);

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
    private string codexServiceTier;
    private IReadOnlyList<CodexModel> catalogModels = [];
    private CodexSpeedChoice[] speedChoices = [];
    public CodexSpeedChoice[] SpeedChoices => speedChoices;
    public string SpeedDescription => SpeedChoices.FirstOrDefault(x => x.Id == CodexServiceTier)?.Description ?? "";
    public string CodexServiceTier
    {
        get => codexServiceTier;
        set
        {
            if (value is null) return;
            if (SetProperty(ref codexServiceTier, value)) OnPropertyChanged(nameof(SpeedDescription));
        }
    }
    private string flipKey;
    private string againKey;
    private string hardKey;
    private string goodKey;
    private string easyKey;
    private string speakKey;
    private string speakExampleKey;
    private bool autoSpeakWord;
    public bool AutoSpeakWord { get => autoSpeakWord; set => SetProperty(ref autoSpeakWord, value); }
    private bool autoSpeakExamples;
    private string? verifiedModelPath;
    private CodexModelChoice[] modelChoices = [];
    private string modelStatus = "開啟此頁時會向 Codex 查詢模型清單。";
    public CodexModelChoice[] ModelChoices => modelChoices;
    public string ModelStatus { get => modelStatus; private set => SetProperty(ref modelStatus, value); }
    public string ModelDescription => ModelChoices.FirstOrDefault(x => x.Model == CodexModel)?.Description ?? "";
    public bool AutoSpeakExamples { get => autoSpeakExamples; set => SetProperty(ref autoSpeakExamples, value); }
    public string DataDirectory { get; }
    public string SpeechStatus { get; }
    public string DailyNewLimitHelp => $"預設 {ApplicationConfiguration.Current.Review.DailyNewLimit} 個，可填入任意非負整數，沒有固定數量上限；0 代表只複習已開始的卡。到期少於 {ApplicationConfiguration.Current.Review.ReduceNewAtDueCount} 張時依你設定；{ApplicationConfiguration.Current.Review.ReduceNewAtDueCount}–{ApplicationConfiguration.Current.Review.PauseNewAtDueCount - 1} 張時最多 {ApplicationConfiguration.Current.Review.ReducedNewLimit} 個，{ApplicationConfiguration.Current.Review.PauseNewAtDueCount} 張以上先不加新詞。所有主題共用每日名額。";
    public string DailyGenerationLimitHelp => $"每日上限（0–{ApplicationConfiguration.Current.Ai.MaximumDailyGenerations} 次）";
    public string DailyNewLimit { get => dailyNewLimit; set => SetProperty(ref dailyNewLimit, value); }
    public string DailyGenerationLimit { get => dailyGenerationLimit; set => SetProperty(ref dailyGenerationLimit, value); }
    public string CodexPath
    {
        get => codexPath;
        set
        {
            if (!SetProperty(ref codexPath, value)) return;
            verifiedModelPath = null;
            UpdateModelChoices([], false);
            ModelStatus = "執行檔路徑已變更，請更新模型清單。";
        }
    }
    public string CodexModel
    {
        get => codexModel;
        set
        {
            // ItemsSource replacement temporarily clears ComboBox.SelectedValue.
            // Preserve the user's selection until the new choices are bound.
            if (value is null) return;
            if (SetProperty(ref codexModel, value))
            {
                OnPropertyChanged(nameof(ModelDescription));
                UpdateSpeedChoices();
            }
        }
    }
    public IReadOnlyList<string> ShortcutKeys => AppSettings.ShortcutKeys;
    public string FlipKey { get => flipKey; set => SetProperty(ref flipKey, value); }
    public string AgainKey { get => againKey; set => SetProperty(ref againKey, value); }
    public string HardKey { get => hardKey; set => SetProperty(ref hardKey, value); }
    public string GoodKey { get => goodKey; set => SetProperty(ref goodKey, value); }
    public string EasyKey { get => easyKey; set => SetProperty(ref easyKey, value); }
    public string SpeakKey { get => speakKey; set => SetProperty(ref speakKey, value); }
    public string SpeakExampleKey { get => speakExampleKey; set => SetProperty(ref speakExampleKey, value); }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CheckAiCommand { get; }
    public AsyncCommand RefreshModelsCommand { get; }
    public RelayCommand CancelModelRefreshCommand { get; }
    public AsyncCommand BackupCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand FindCodexCommand { get; }
    public RelayCommand BrowseCodexCommand { get; }
    public AsyncCommand ImportVocabularyCommand { get; }
    public AsyncCommand ExportVocabularyCommand { get; }

    public SettingsViewModel(IBackupService backup, IContentGenerator generator, IPronunciationService speech,
        AppSettings settings, AiSettings aiSettings, string dataDirectory, Action restored, IStudyStore? store = null)
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
        codexServiceTier = settings.CodexServiceTier;
        UpdateModelChoices([], false);
        flipKey = settings.FlipKey;
        againKey = settings.AgainKey;
        hardKey = settings.HardKey;
        goodKey = settings.GoodKey;
        easyKey = settings.EasyKey;
        speakKey = settings.SpeakKey;
        speakExampleKey = settings.SpeakExampleKey;
        autoSpeakWord = settings.AutoSpeakWord;
        autoSpeakExamples = settings.AutoSpeakExamples;
        SaveCommand = Command(SaveAsync);
        RefreshModelsCommand = Command(RefreshModelsAsync);
        CancelModelRefreshCommand = new(_ => RefreshModelsCommand.Cancel());
        CheckAiCommand = Command(async token =>
        {
            await SaveAsync(token);
            Notice = await generator.CheckAvailabilityAsync(token);
            await RefreshModelsAsync(token);
        });
        BackupCommand = Command(BackupAsync);
        RestoreCommand = Command(RestoreAsync);
        ImportVocabularyCommand = Command(async token =>
        {
            var dialog = new OpenFileDialog { Title = "匯入單字 JSON", Filter = "單字 JSON (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            var count = await new VocabularyFileService(store!).ImportAsync(dialog.FileName, token);
            Notice = $"已匯入 {count} 個詞義，既有複習進度已保留。";
        }, () => store is not null);
        ExportVocabularyCommand = Command(async token =>
        {
            var dialog = new SaveFileDialog { Title = "匯出單字 JSON", Filter = "單字 JSON (*.json)|*.json", FileName = "vocabulary.json" };
            if (dialog.ShowDialog() != true) return;
            var count = await new VocabularyFileService(store!).ExportAsync(dialog.FileName, token);
            Notice = $"已匯出 {count} 個詞義：{dialog.FileName}";
        }, () => store is not null);
        OpenFolderCommand = new(_ => OpenFolder());
        FindCodexCommand = new(_ =>
        {
            try { CodexPath = CodexExecutableLocator.Resolve("codex"); Error = ""; Notice = "已找到 Codex。按「保存並檢查」確認登入。"; }
            catch (Exception exception) { Error = exception.Message; }
        });
        BrowseCodexCommand = new(_ =>
        {
            var dialog = new OpenFileDialog { Title = "選擇 Codex 執行檔", Filter = "Codex 執行檔 (*.exe)|*.exe" };
            if (dialog.ShowDialog() == true) CodexPath = dialog.FileName;
        });
    }

    public override Task LoadAsync(CancellationToken cancellationToken = default) =>
        RefreshModelsCommand.ExecuteAsync();

    private void UpdateModelChoices(IReadOnlyList<CodexModel> models, bool verified)
    {
        catalogModels = models;
        var choices = new List<CodexModelChoice>
        {
            new("", "使用 CLI 預設（自動跟隨 Codex）", "讓 Codex 選擇預設模型，不固定指定模型名稱。", true)
        };
        choices.AddRange(models.Select(x => new CodexModelChoice(x.Model,
            x.DisplayName + (x.IsDefault ? " · Codex 建議" : ""), x.Description, true)));
        if (!string.IsNullOrEmpty(CodexModel) && choices.All(x => x.Model != CodexModel))
            choices.Add(new(CodexModel, CodexModel + (verified ? " · 目前清單未提供" : " · 上次選擇，尚未確認"),
                verified ? "請改選清單中的模型或使用 CLI 預設，再保存設定。" : "更新模型清單後可確認這個模型是否仍可使用。", false));
        modelChoices = choices.ToArray();
        OnPropertyChanged(nameof(ModelChoices));
        OnPropertyChanged(nameof(CodexModel));
        OnPropertyChanged(nameof(ModelDescription));
        UpdateSpeedChoices();
    }

    private void UpdateSpeedChoices()
    {
        var model = string.IsNullOrEmpty(CodexModel) ? catalogModels.FirstOrDefault(x => x.IsDefault)
            : catalogModels.FirstOrDefault(x => x.Model == CodexModel);
        var choices = new List<CodexSpeedChoice>
        {
            new("", "標準", "使用標準生成速度，較省訂閱額度。", true)
        };
        if (model is not null)
            choices.AddRange(model.ServiceTiers.Select(x => new CodexSpeedChoice(x.Id, x.Name, x.Description, true)));
        if (!string.IsNullOrEmpty(CodexServiceTier) && choices.All(x => x.Id != CodexServiceTier))
            choices.Add(new(CodexServiceTier, CodexServiceTier + " · 尚未確認可用",
                "目前模型清單未提供這個模式；更新清單後改選可用模式或標準。", false));
        speedChoices = choices.ToArray();
        OnPropertyChanged(nameof(SpeedChoices));
        OnPropertyChanged(nameof(CodexServiceTier));
        OnPropertyChanged(nameof(SpeedDescription));
    }

    private async Task RefreshModelsAsync(CancellationToken token)
    {
        if (generator is not ICodexModelCatalog catalog)
        { ModelStatus = "目前生成器不提供模型清單，仍可使用 CLI 預設。"; return; }
        var requestedPath = CodexPath.Trim();
        verifiedModelPath = null;
        ModelStatus = "正在向 Codex 查詢模型清單…";
        try
        {
            var models = await catalog.ListModelsAsync(requestedPath, token);
            token.ThrowIfCancellationRequested();
            if (requestedPath != CodexPath.Trim())
            { ModelStatus = "執行檔路徑已變更，請重新更新模型清單。"; return; }
            UpdateModelChoices(models, true);
            verifiedModelPath = requestedPath;
            ModelStatus = $"已取得 {models.Count} 個模型。清單由 Codex CLI 提供，可能使用快取；CLI 更新後可重新查詢。";
        }
        catch (OperationCanceledException) { ModelStatus = "模型清單更新已取消，原設定保留。"; throw; }
        catch (Exception exception)
        {
            ModelStatus = requestedPath == CodexPath.Trim() ? $"暫時無法更新模型清單：{exception.Message}"
                : "執行檔路徑已變更，請重新更新模型清單。";
        }
    }

    private Task SaveAsync(CancellationToken token)
    {
        if (!BigInteger.TryParse(DailyNewLimit.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var newLimit))
            throw new InvalidOperationException("每日新詞上限請填入非負整數；0 代表暫停新詞，數量沒有固定上限。");
        if (!int.TryParse(DailyGenerationLimit, out var generationLimit) || generationLimit < 0 || generationLimit > ApplicationConfiguration.Current.Ai.MaximumDailyGenerations)
            throw new InvalidOperationException($"每日 AI 次數請填入 0–{ApplicationConfiguration.Current.Ai.MaximumDailyGenerations} 的整數；0 代表停用生成。");
        if (string.IsNullOrWhiteSpace(CodexPath)) throw new InvalidOperationException("請填入 Codex 執行檔路徑或 codex。");
        if (verifiedModelPath == CodexPath.Trim() && !ModelChoices.Any(x => x.Model == CodexModel && x.IsAvailable))
            throw new InvalidOperationException("所選模型未出現在目前 Codex 清單，請改選其他模型或使用 CLI 預設。");
        if (verifiedModelPath == CodexPath.Trim() && !SpeedChoices.Any(x => x.Id == CodexServiceTier && x.IsAvailable))
            throw new InvalidOperationException("所選加速模式未出現在目前模型清單，請改選可用模式或標準。");
        var updated = new AppSettings
        {
            Practice = settings.Practice, DailyNewLimit = newLimit, CodexExecutablePath = CodexPath.Trim(), CodexModel = CodexModel.Trim(),
            CodexServiceTier = CodexServiceTier,
            DailyGenerationLimit = generationLimit, FlipKey = FlipKey, AgainKey = AgainKey,
            HardKey = HardKey, GoodKey = GoodKey, EasyKey = EasyKey, SpeakKey = SpeakKey, SpeakExampleKey = SpeakExampleKey,
            AutoSpeakWord = AutoSpeakWord, AutoSpeakExamples = AutoSpeakExamples
        };
        updated.Save(DataDirectory);
        settings.DailyNewLimit = updated.DailyNewLimit;
        settings.CodexExecutablePath = updated.CodexExecutablePath;
        settings.CodexModel = updated.CodexModel;
        settings.CodexServiceTier = updated.CodexServiceTier;
        settings.DailyGenerationLimit = updated.DailyGenerationLimit;
        settings.FlipKey = updated.FlipKey;
        settings.AgainKey = updated.AgainKey;
        settings.HardKey = updated.HardKey;
        settings.GoodKey = updated.GoodKey;
        settings.EasyKey = updated.EasyKey;
        settings.SpeakKey = updated.SpeakKey;
        settings.SpeakExampleKey = updated.SpeakExampleKey;
        settings.AutoSpeakWord = updated.AutoSpeakWord;
        settings.AutoSpeakExamples = updated.AutoSpeakExamples;
        aiSettings.ExecutablePath = settings.CodexExecutablePath;
        aiSettings.Model = settings.CodexModel;
        aiSettings.ServiceTier = settings.CodexServiceTier;
        aiSettings.DailyGenerationLimit = generationLimit;
        Notice = "設定已保存。快捷鍵與新詞上限從下一次進入複習／取卡開始套用。";
        return Task.CompletedTask;
    }

    private async Task BackupAsync(CancellationToken token)
    {
        var backupDirectory = Path.Combine(DataDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var dialog = new SaveFileDialog
        {
            Title = "備份單字庫", Filter = "Wording 備份 (*.zip)|*.zip", DefaultExt = ".zip",
            FileName = $"Wording-{DateTime.Now:yyyyMMdd-HHmmss}.zip", InitialDirectory = backupDirectory
        };
        if (dialog.ShowDialog() != true) return;
        await backup.CreateBackupAsync(dialog.FileName, token);
        Notice = $"備份已完成：{dialog.FileName}";
    }

    private async Task RestoreAsync(CancellationToken token)
    {
        var dialog = new OpenFileDialog { Title = "選擇要還原的 Wording 備份", Filter = "Wording 備份 (*.zip;*.wtbackup)|*.zip;*.wtbackup" };
        if (dialog.ShowDialog() != true) return;
        if (!Views.ConfirmationDialog.Confirm(Views.ConfirmationContent.RestoreBackup(dialog.FileName))) return;
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
