using System.Windows;

namespace Wording.Desktop.Views;

public partial class LeaveEditorDialog : Window
{
    public LeaveEditorDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => KeepEditing.Focus();
    }
    public static bool Confirm()
    {
        var dialog = new LeaveEditorDialog();
        var owner = Application.Current?.MainWindow;
        if (owner?.IsVisible == true) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
    private void Discard(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Keep(object sender, RoutedEventArgs e) => DialogResult = false;
}
