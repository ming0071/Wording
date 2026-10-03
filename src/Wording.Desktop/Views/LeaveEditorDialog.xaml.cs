using System.Windows;

namespace Wording.Desktop.Views;

public partial class LeaveEditorDialog : Window
{
    public LeaveEditorDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => KeepEditing.Focus();
    }
    public static LeaveEditorDialog CreatePracticeDialog()
    {
        var dialog = new LeaveEditorDialog { Title = "開始新的練習" };
        dialog.ContextLabel.Text = "尚未提交的練習";
        dialog.Heading.Text = "要取代當次練習嗎？";
        dialog.Message.Text = "切換閱讀／聽力或生成新的練習，會取代當次尚未提交的文章與答案。你也可以留在這裡，繼續完成作答。";
        dialog.DiscardChanges.Content = "取代並繼續";
        dialog.KeepEditing.Content = "繼續作答";
        return dialog;
    }
    public static bool ConfirmPractice() => ShowConfirmation(CreatePracticeDialog());
    public static bool Confirm() => ShowConfirmation(new LeaveEditorDialog());
    private static bool ShowConfirmation(LeaveEditorDialog dialog)
    {
        var owner = Application.Current?.MainWindow;
        if (owner?.IsVisible == true) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
    private void Discard(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Keep(object sender, RoutedEventArgs e) => DialogResult = false;
}
