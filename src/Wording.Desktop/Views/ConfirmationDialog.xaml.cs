using System.Windows;

namespace Wording.Desktop.Views;

public sealed record ConfirmationContent(string Title, string Context, string Heading,
    string Message, string ConfirmText, string CancelText)
{
    public static ConfirmationContent LeaveEditor { get; } = new("尚未保存", "編輯中的詞義", "要離開編輯嗎？",
        "還有尚未保存的修改。你可以繼續編輯並保存，或捨棄這次修改後離開。", "捨棄並離開", "繼續編輯");

    public static ConfirmationContent ReplacePractice { get; } = new("開始新的練習", "尚未提交的練習", "要取代當次練習嗎？",
        "切換閱讀／聽力或生成新的練習，會取代當次尚未提交的文章與答案。你也可以留在這裡，繼續完成作答。", "取代並繼續", "繼續作答");

    public static ConfirmationContent RestoreBackup(string path) => new("還原整個單字庫", "資料與備份", "要還原這份備份嗎？",
        $"將用以下備份替換目前整個單字庫與複習紀錄：\n{path}\n\n程式會先保存現有資料的復原備份。", "還原備份", "保留目前資料");
}

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(ConfirmationContent content)
    {
        InitializeComponent();
        Title = content.Title;
        ContextLabel.Text = content.Context;
        Heading.Text = content.Heading;
        Message.Text = content.Message;
        ConfirmAction.Content = content.ConfirmText;
        CancelAction.Content = content.CancelText;
        Loaded += (_, _) => CancelAction.Focus();
    }

    public static bool Confirm(ConfirmationContent content)
    {
        var dialog = new ConfirmationDialog(content);
        var owner = Application.Current?.MainWindow;
        if (owner?.IsVisible == true) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    private void ConfirmActionClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelActionClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
