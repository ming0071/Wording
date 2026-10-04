using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Desktop;

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
            AppDiagnostics.Initialize(dataDirectory);
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..20];
            instanceMutex = new Mutex(true, $"Local\\Wording-{identity}", out var firstInstance);
            if (!firstInstance)
            {
                if (VocabularyFileCommand.IsRequested(e.Args)) throw new InvalidOperationException("請先關閉使用這個單字庫的 Wording，再執行檔案匯入或匯出。");
                MessageBox.Show("這個單字庫已在另一個 Wording 視窗開啟。", "Wording");
                Shutdown();
                return;
            }
            var settings = AppSettings.Load(dataDirectory);
            var aiSettings = new AiSettings { ExecutablePath = settings.CodexExecutablePath, Model = settings.CodexModel, ServiceTier = settings.CodexServiceTier, DailyGenerationLimit = settings.DailyGenerationLimit };
            var store = new SqliteStudyStore(AppDataPaths.DatabasePath(dataDirectory));
            AppDiagnostics.Write($"Database path={store.DatabasePath}; existing={File.Exists(store.DatabasePath)}; bytes={(File.Exists(store.DatabasePath) ? new FileInfo(store.DatabasePath).Length : 0)}; user={System.Security.Principal.WindowsIdentity.GetCurrent().Name}; exe={Environment.ProcessPath}");
            await store.InitializeAsync();
            var removedDuplicates = await store.MergeDuplicateWordsAsync();
            if (removedDuplicates > 0) AppDiagnostics.Write($"Combined duplicate word rows={removedDuplicates}; affected words reset to new.");
            var cleanedNotes = await store.RemoveVocabularyNoteMetadataAsync();
            if (cleanedNotes > 0) AppDiagnostics.Write($"Removed vocabulary note boilerplate: words={cleanedNotes}.");
            AppDiagnostics.Write($"Database initialized: bytes={new FileInfo(store.DatabasePath).Length}; words={(await store.GetVocabularyAsync()).Count}; categories={(await store.GetCategoriesAsync()).Count}");
            if (VocabularyFileCommand.IsRequested(e.Args))
            {
                Shutdown(await VocabularyFileCommand.ExecuteAsync(e.Args, store));
                return;
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
            AppDiagnostics.Write($"Startup error: {exception}");
            if (VocabularyFileCommand.IsRequested(e.Args))
            {
                try { VocabularyFileCommand.WriteResult(e.Args, false, exception.Message); }
                finally { Shutdown(1); }
                return;
            }
            MessageBox.Show($"Wording 無法啟動。既有資料不會被自動清空。\n\n{exception.Message}", "啟動失敗", MessageBoxButton.OK, MessageBoxImage.Error);
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
        return AppDataPaths.DefaultDirectory();
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
