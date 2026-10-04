using System.Windows.Automation;
using System.Windows.Controls;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task ModelPickerRefreshUsesUnsavedExecutableAndPreservesSelectionUntilUserSaves()
    {
        await OffscreenWpf.InvokeAsync(async () =>
        {
            using var data = new StudyTestData();
            var settings = new AppSettings { CodexModel = "future-model" };
            var generator = new ModelGenerator();
            var vm = new SettingsViewModel(new BackupService(data.Store), generator, new SilentSpeech(), settings, new(), data.DirectoryPath, () => { });
            var view = new SettingsView { DataContext = vm, FontSize = 14,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["InkBrush"],
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 247, 251)) };
            Layout(view, 715, 1600);
            var picker = Descendants(view).OfType<ComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "CodexModelPicker");
            vm.CodexPath = "new-codex.exe";
            await vm.LoadAsync();
            Layout(view, 715, 1600);
            Assert.Equal("new-codex.exe", generator.RequestedPath);
            Assert.Equal("future-model", vm.CodexModel);
            Assert.Equal("future-model", picker.SelectedValue);
            Assert.Contains(vm.ModelChoices, x => x.Model == "new-model" && x.Label.Contains("New Model"));
            picker.SelectedValue = "new-model";
            Assert.Equal("new-model", vm.CodexModel);
            Assert.Equal("future-model", settings.CodexModel);
            await vm.SaveCommand.ExecuteAsync();
            Assert.Equal("new-model", settings.CodexModel);
            Assert.Equal("new-model", AppSettings.Load(data.DirectoryPath).CodexModel);
            Layout(view, 715, 1600);
            Assert.Contains(Descendants(picker).OfType<TextBlock>(), x => x.Text == "New Model");
            Render(view, "settings-model-picker.png");
            picker.SelectedValue = "";
            Layout(view, 715, 1600);
            Assert.Empty(vm.CodexModel);
            Assert.Contains(Descendants(picker).OfType<TextBlock>(), x => x.Text.Contains("使用 CLI 預設"));
        });
    }

    [Fact]
    public async Task RetiredSelectionIsKeptVisibleButRequiresReselectionAndDefaultSavesEmptyModel()
    {
        using var data = new StudyTestData();
        var settings = new AppSettings { CodexModel = "retired-model" };
        var vm = new SettingsViewModel(new BackupService(data.Store), new ModelGenerator(), new SilentSpeech(), settings, new(), data.DirectoryPath, () => { });
        await vm.LoadAsync();
        Assert.Equal("retired-model", vm.CodexModel);
        Assert.False(vm.ModelChoices.Single(x => x.Model == "retired-model").IsAvailable);
        await vm.SaveCommand.ExecuteAsync();
        Assert.Contains("改選", vm.Error);
        Assert.False(File.Exists(Path.Combine(data.DirectoryPath, "settings.json")));
        vm.CodexModel = "";
        await vm.SaveCommand.ExecuteAsync();
        Assert.Empty(vm.Error);
        Assert.Empty(AppSettings.Load(data.DirectoryPath).CodexModel);
    }

    [Fact]
    public async Task FailedRefreshKeepsExistingSettingsAndDefaultOptionUsable()
    {
        using var data = new StudyTestData();
        var settings = new AppSettings();
        var generator = new ModelGenerator { Query = (_, _) => throw new TimeoutException("test timeout") };
        var vm = new SettingsViewModel(new BackupService(data.Store), generator, new SilentSpeech(), settings, new(), data.DirectoryPath, () => { });
        await vm.LoadAsync();
        Assert.Contains("暫時無法", vm.ModelStatus);
        Assert.Empty(vm.Error);
        Assert.True(vm.ModelChoices.Single(x => x.Model == "").IsAvailable);
        await vm.SaveCommand.ExecuteAsync();
        Assert.Empty(vm.Error);
        Assert.Empty(settings.CodexModel);
    }

    [Fact]
    public async Task LateResponseFromPreviousExecutableCannotPopulateNewExecutableChoices()
    {
        using var data = new StudyTestData();
        var pending = new TaskCompletionSource<IReadOnlyList<CodexModel>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new ModelGenerator { Query = (_, _) => pending.Task };
        var vm = new SettingsViewModel(new BackupService(data.Store), generator, new SilentSpeech(), new(), new(), data.DirectoryPath, () => { });
        var load = vm.LoadAsync();
        vm.CodexPath = "different-codex.exe";
        pending.SetResult([new("wrong-catalog-model", "Wrong", "", true)]);
        await load;
        Assert.DoesNotContain(vm.ModelChoices, x => x.Model == "wrong-catalog-model");
        Assert.Contains("路徑已變更", vm.ModelStatus);
    }

    [Fact]
    public async Task SpeedPickerUsesDefaultModelCapabilitiesAndSavesOnlyExplicitSelection()
    {
        await OffscreenWpf.InvokeAsync(async () =>
        {
            using var data = new StudyTestData();
            var settings = new AppSettings();
            var ai = new AiSettings();
            var generator = new ModelGenerator { Query = (_, _) => Task.FromResult<IReadOnlyList<CodexModel>>([
                new("fast-model", "Fast Model", "", true) { ServiceTiers = [new("priority", "Fast", "2x speed, increased usage"), new("future_speed", "Future speed", "Future description")] },
                new("standard-only", "Standard Model", "", false)]) };
            var vm = new SettingsViewModel(new BackupService(data.Store), generator, new SilentSpeech(), settings, ai, data.DirectoryPath, () => { });
            var view = new SettingsView { DataContext = vm, FontSize = 14,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["InkBrush"],
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 247, 251)) };
            await vm.LoadAsync();
            Layout(view, 715, 1900);
            var picker = Descendants(view).OfType<ComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "CodexSpeedPicker");
            Assert.Equal("", picker.SelectedValue);
            Assert.Contains(vm.SpeedChoices, x => x.Id == "future_speed");
            picker.SelectedValue = "priority";
            Assert.Equal("2x speed, increased usage", vm.SpeedDescription);
            Assert.Empty(ai.ServiceTier);
            await vm.SaveCommand.ExecuteAsync();
            Assert.Empty(vm.Error);
            Assert.Equal("priority", AppSettings.Load(data.DirectoryPath).CodexServiceTier);
            Assert.Equal("priority", ai.ServiceTier);
            Layout(view, 715, 1900);
            Assert.Contains(Descendants(picker).OfType<TextBlock>(), x => x.Text == "Fast");
            Render(view, "settings-speed-picker.png");
            vm.CodexModel = "standard-only";
            Layout(view, 715, 1900);
            Assert.Equal("priority", vm.CodexServiceTier);
            await vm.SaveCommand.ExecuteAsync();
            Assert.Contains("加速模式", vm.Error);
            picker.SelectedValue = "";
            await vm.SaveCommand.ExecuteAsync();
            Assert.Empty(vm.Error);
            Assert.Empty(ai.ServiceTier);
        });
    }

    [Fact]
    public void OldSettingsDefaultToStandardAndUnsafeSpeedValuesAreRejected()
    {
        using var data = new StudyTestData();
        File.WriteAllText(Path.Combine(data.DirectoryPath, "settings.json"), "{\"DailyNewLimit\":5}");
        Assert.Empty(AppSettings.Load(data.DirectoryPath).CodexServiceTier);
        var settings = new AppSettings { CodexServiceTier = "priority\"\nmodel=other" };
        Assert.Throws<ArgumentException>(() => settings.Save(data.DirectoryPath));
        Assert.Throws<ArgumentException>(() => new CodexContentGenerator(new() { ServiceTier = settings.CodexServiceTier }, data.DirectoryPath).BuildArguments("schema.json"));
    }

    private sealed class ModelGenerator : IContentGenerator, ICodexModelCatalog
    {
        public string? RequestedPath { get; private set; }
        public Func<string, CancellationToken, Task<IReadOnlyList<CodexModel>>> Query { get; set; } = (_, _) =>
            Task.FromResult<IReadOnlyList<CodexModel>>([new("future-model", "Future Model", "Future description", true), new("new-model", "New Model", "New description", false)]);
        public Task<IReadOnlyList<CodexModel>> ListModelsAsync(string executable, CancellationToken cancellationToken = default)
        { RequestedPath = executable; return Query(executable, cancellationToken); }
        public Task<string> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult("test login");
        public Task<AiEnrichment> GenerateAsync(VocabularyItem word, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
