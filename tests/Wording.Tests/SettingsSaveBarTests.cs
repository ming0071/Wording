using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task SettingsKeepSaveVisibleAndOnlyClearPendingChangesAfterSuccessfulSave()
    {
        await OffscreenWpf.InvokeAsync(async () =>
        {
            using var data = new StudyTestData();
            var settings = new AppSettings(); settings.Save(data.DirectoryPath);
            var original = File.ReadAllText(Path.Combine(data.DirectoryPath, "settings.json"));
            var main = new MainViewModel(data.Store, new BackupService(data.Store), new ModelGenerator(), new SilentSpeech(), settings, new(), data.DirectoryPath);
            var vm = main.Settings;
            var view = new SettingsView { DataContext = vm, FontSize = 14, FontFamily = new("Segoe UI, Microsoft JhengHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            Assert.False(vm.HasUnsavedChanges);
            await vm.LoadAsync(); Assert.Empty(vm.Error); Assert.False(vm.HasUnsavedChanges);
            vm.AutoSpeakWord = !settings.AutoSpeakWord; Assert.True(vm.HasUnsavedChanges);
            vm.AutoSpeakWord = settings.AutoSpeakWord; Assert.False(vm.HasUnsavedChanges);
            vm.CodexModel = "new-model"; Assert.True(vm.HasUnsavedChanges);
            vm.CodexModel = settings.CodexModel; Assert.False(vm.HasUnsavedChanges);
            Layout(view, 715, 560);
            var input = Descendants(view).OfType<TextBox>().Single(x => AutomationProperties.GetAutomationId(x) == "DailyNewLimit");
            input.Text = "invalid";
            Assert.True(vm.HasUnsavedChanges);
            await vm.SaveCommand.ExecuteAsync();
            Assert.NotEmpty(vm.Error); Assert.True(vm.HasUnsavedChanges);
            Assert.Equal(original, File.ReadAllText(Path.Combine(data.DirectoryPath, "settings.json")));
            input.Text = "42";
            foreach (var width in new[] { 715, 959 })
            {
                Layout(view, width, 560);
                var scroll = (ScrollViewer)view.FindName("SettingsScroll");
                scroll.ScrollToBottom(); Layout(view, width, 560);
                AssertFits(view, "SaveSettings"); AssertFits(view, "SettingsSaveStatus");
                Assert.True(scroll.ScrollableHeight > 0);
                Render(view, $"settings-savebar-pending-{width}.png");
                scroll.ScrollToTop(); Layout(view, width, 560); AssertFits(view, "SaveSettings");
            }
            await vm.SaveCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Assert.False(vm.HasUnsavedChanges);
            Assert.Equal(42, (int)AppSettings.Load(data.DirectoryPath).DailyNewLimit);
            Layout(view, 715, 560);
            AssertFits(view, "SaveSettings"); AssertFits(view, "SettingsSaveStatus");
            Render(view, "settings-savebar-saved.png");
        });
    }
}
