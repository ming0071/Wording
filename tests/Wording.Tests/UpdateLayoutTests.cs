using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Wording.Core;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task UpdatePanelAndBannerFitNarrowAndWideWindows()
    {
        using var data = new StudyTestData();
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var vm = new UpdateViewModel(new UpdateUiClient(), new UpdateUiInstaller(), new UpdateUiBackup(), new(),
                new(0, 5, 0), data.DirectoryPath, () => { }, () => null, () => { });
            await vm.CheckCommand.ExecuteAsync();
            foreach (var width in new[] { 715, 959 })
            {
                var panel = new UpdatePanel { DataContext = vm, FontSize = 14, Foreground = (Brush)Application.Current.Resources["InkBrush"],
                    FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
                Layout(panel, width, 500);
                AssertFits(panel, "CheckForUpdates");
                foreach (var button in Descendants(panel).OfType<Button>().Where(IsShown))
                {
                    var bounds = button.TransformToAncestor(panel).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Assert.InRange(bounds.Right, 0, panel.ActualWidth + 0.1);
                    Assert.InRange(bounds.Bottom, 0, panel.ActualHeight + 0.1);
                }
                Render(panel, $"update-v050-settings-{width}.png");
                var banner = new UpdateBanner { DataContext = vm, FontSize = 14 };
                Layout(banner, width, 180);
                Assert.Contains(Descendants(banner).OfType<Button>(), x => Equals(x.Content, "下載並更新"));
                Render(banner, $"update-v050-banner-{width}.png");
            }
        });
    }

    [Fact]
    public async Task GlobalUpdateBannerIsHiddenOnSettingsWhereTheSameUpdateIsShown()
    {
        using var data = new StudyTestData();
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var main = new MainViewModel(new RecordingStore(), new UpdateUiBackup(), new FixedGenerator(), new SilentSpeech(),
                new(), new(), data.DirectoryPath, new UpdateUiClient(), new UpdateUiInstaller());
            await main.Updates.CheckCommand.ExecuteAsync();
            Assert.True(main.ShowUpdateBanner);
            main.NavigateCommand.Execute("settings");
            Assert.True(main.IsSettingsSelected);
            Assert.False(main.ShowUpdateBanner);
            Assert.Same(main.Updates, main.Settings.Updates);
        });
    }

    private sealed class UpdateUiClient : IApplicationUpdateClient
    {
        public Task<ApplicationRelease?> CheckAsync(Version current, CancellationToken token = default) =>
            Task.FromResult<ApplicationRelease?>(new(new(0, 5, 1), "改善複習閱讀與教材選擇。\n更新後沿用既有詞庫與學習紀錄。",
                new("https://github.com/ming0071/Wording/releases/tag/v0.5.1"),
                new("https://github.com/ming0071/Wording/releases/download/v0.5.1/Wording-0.5.1-win-x64-Setup.exe"), new('a', 64), 100));
        public Task<DownloadedUpdate> DownloadAsync(ApplicationRelease release, string cache, IProgress<UpdateProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class UpdateUiInstaller : IUpdateInstaller
    { public Task LaunchAsync(DownloadedUpdate update, string directory, CancellationToken token = default) => throw new NotSupportedException(); }
    private sealed class UpdateUiBackup : IBackupService
    {
        public Task CreateBackupAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task RestoreBackupAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
