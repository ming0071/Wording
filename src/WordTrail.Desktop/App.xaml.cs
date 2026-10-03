using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using WordTrail.Core;
using WordTrail.Desktop.ViewModels;
using WordTrail.Infrastructure;

namespace WordTrail.Desktop;

public partial class App : Application
{
    private IPronunciationService? speech;
    private Mutex? instanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var dataDirectory = GetDataDirectory(e.Args);
            Directory.CreateDirectory(dataDirectory);
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..20];
            instanceMutex = new Mutex(true, $"Local\\WordTrail-{identity}", out var firstInstance);
            if (!firstInstance)
            {
                if (VocabularyFileCommand.IsRequested(e.Args)) throw new InvalidOperationException("請先關閉使用這個單字庫的 WordTrail，再執行檔案匯入或匯出。");
                MessageBox.Show("這個單字庫已在另一個 WordTrail 視窗開啟。", "WordTrail");
                Shutdown();
                return;
            }
            var settings = AppSettings.Load(dataDirectory);
            var aiSettings = new AiSettings { ExecutablePath = settings.CodexExecutablePath, Model = settings.CodexModel, DailyGenerationLimit = settings.DailyGenerationLimit };
            var store = new SqliteStudyStore(Path.Combine(dataDirectory, "wordtrail.db"));
            await store.InitializeAsync();
            if (VocabularyFileCommand.IsRequested(e.Args))
            {
                Shutdown(await VocabularyFileCommand.ExecuteAsync(e.Args, store));
                return;
            }
            var seedPath = Path.Combine(AppContext.BaseDirectory, "content", "toeic-starter.json");
            if (File.Exists(seedPath))
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                options.Converters.Add(new JsonStringEnumConverter());
                var pack = JsonSerializer.Deserialize<SeedPack>(await File.ReadAllTextAsync(seedPath), options)
                    ?? throw new InvalidDataException("起始教材格式無法讀取。");
                await store.ImportSeedPackAsync(pack);
            }
            var backup = new BackupService(store);
            var generator = new CodexContentGenerator(aiSettings, Path.Combine(dataDirectory, "AiWorkspace"));
            speech = new WindowsPronunciationService();
            var viewModel = new MainViewModel(store, backup, generator, speech, settings, aiSettings, dataDirectory);
            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            await viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            if (VocabularyFileCommand.IsRequested(e.Args))
            {
                try { VocabularyFileCommand.WriteResult(e.Args, false, exception.Message); }
                finally { Shutdown(1); }
                return;
            }
            MessageBox.Show($"WordTrail 無法啟動。既有資料不會被自動清空。\n\n{exception.Message}", "啟動失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string GetDataDirectory(string[] arguments)
    {
        var index = Array.IndexOf(arguments, "--data-dir");
        if (index >= 0)
        {
            if (index + 1 >= arguments.Length) throw new ArgumentException("--data-dir 後需提供獨立的資料目錄。");
            return Path.GetFullPath(arguments[index + 1]);
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordTrail");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        speech?.Dispose();
        if (instanceMutex is not null)
        {
            try { instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
