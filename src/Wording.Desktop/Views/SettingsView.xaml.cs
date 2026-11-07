using System.Windows.Controls;
using System.Windows.Threading;
using Wording.Desktop.ViewModels;
namespace Wording.Desktop.Views;
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsLoaded && DataContext is SettingsViewModel model && model.TakeCodexFocusRequest())
            {
                CodexPathInput.BringIntoView();
                CodexPathInput.Focus();
            }
        }));
    }
}
