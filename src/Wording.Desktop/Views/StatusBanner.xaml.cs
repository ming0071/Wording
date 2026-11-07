using System.Windows.Controls;
using System.Windows;
namespace Wording.Desktop.Views;
public partial class StatusBanner : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(StatusBanner), new PropertyMetadata(false));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public StatusBanner() => InitializeComponent();
}
