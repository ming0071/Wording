using System.Windows;
using System.Windows.Controls;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public void LeaveEditorDialogUsesAppStylesAndSafeDefault()
    {
        OffscreenWpf.Invoke(() =>
        {
            var dialog = new LeaveEditorDialog();
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
            var dialog = LeaveEditorDialog.CreatePracticeDialog();
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
}
