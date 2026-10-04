using System.Windows;
using System.Windows.Controls;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public void EditorConfirmationUsesAppStylesAndSafeDefault()
    {
        OffscreenWpf.Invoke(() =>
        {
            var dialog = new ConfirmationDialog(ConfirmationContent.LeaveEditor);
            var content = (FrameworkElement)dialog.Content;
            Layout(content, 460, 280);
            var buttons = Descendants(content).OfType<Button>().ToArray();
            var keep = Assert.Single(buttons, x => x.Content as string == "繼續編輯");
            Assert.True(keep.IsDefault);
            Assert.True(keep.IsCancel);
            Assert.False(Assert.Single(buttons, x => x.Content as string == "捨棄並離開").IsDefault);
            Render(content, "leave-editor-v018.png");
            dialog.Close();
        });
    }

    [Fact]
    public void PracticeReplacementDialogSharesAppStylesAndKeepsCurrentWorkByDefault()
    {
        OffscreenWpf.Invoke(() =>
        {
            var dialog = new ConfirmationDialog(ConfirmationContent.ReplacePractice);
            var content = (FrameworkElement)dialog.Content;
            Layout(content, 460, 300);
            var buttons = Descendants(content).OfType<Button>().ToArray();
            var keep = Assert.Single(buttons, x => x.Content as string == "繼續作答");
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel);
            Assert.Same(Application.Current.Resources["PrimaryButton"], keep.Style);
            Assert.False(Assert.Single(buttons, x => x.Content as string == "取代並繼續").IsDefault);
            Assert.Equal(WindowStyle.None, dialog.WindowStyle);
            Assert.Equal(new CornerRadius(16), ((Border)content).CornerRadius);
            Assert.Contains(Descendants(content).OfType<TextBlock>(), x => x.Text.Contains("尚未提交的文章與答案"));
            Render(content, "replace-practice-dialog.png");
            dialog.Close();
        });
    }

    [Fact]
    public void BackupConfirmationIdentifiesTheArchiveAndKeepsCurrentDataByDefault()
    {
        OffscreenWpf.Invoke(() =>
        {
            var path = @"C:\Backups\Wording saved.zip";
            var dialog = new ConfirmationDialog(ConfirmationContent.RestoreBackup(path));
            var content = (FrameworkElement)dialog.Content;
            Layout(content, 460, 350);
            var buttons = Descendants(content).OfType<Button>().ToArray();
            var cancel = Assert.Single(buttons, button => button.Content as string == "保留目前資料");
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.False(Assert.Single(buttons, button => button.Content as string == "還原備份").IsDefault);
            Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text.Contains(path) && text.Text.Contains("替換目前整個單字庫"));
            Render(content, "restore-backup-dialog.png");
            dialog.Close();
        });
    }
}
