using System.Windows;
using System.Windows.Controls;
using WordTrail.Desktop.ViewModels;
using WordTrail.Desktop.Views;

namespace WordTrail.Tests;

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
}
