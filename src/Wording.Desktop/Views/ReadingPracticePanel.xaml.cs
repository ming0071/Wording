using System.Windows.Controls;
using Wording.Desktop.ViewModels;

namespace Wording.Desktop.Views;

public partial class ReadingPracticePanel : UserControl
{
    private ReviewExampleViewModel? observed;
    public ReadingPracticePanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Observe();
        Unloaded += (_, _) => StopObserving();
        DataContextChanged += (_, _) => { if (IsLoaded) Observe(); };
    }
    private void Observe()
    {
        StopObserving(); observed = DataContext as ReviewExampleViewModel;
        if (observed is not null) observed.PropertyChanged += Changed;
    }
    private void StopObserving() { if (observed is not null) observed.PropertyChanged -= Changed; observed = null; }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewExampleViewModel.IsSelected) && observed?.IsSelected == true)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { if (IsLoaded) BringIntoView(); }));
    }
}
