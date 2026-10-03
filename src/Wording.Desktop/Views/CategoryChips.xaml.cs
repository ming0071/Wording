using System.Windows.Controls;
using System.Windows;
namespace Wording.Desktop.Views;
public partial class CategoryChips : UserControl
{
    public static readonly DependencyProperty ShowAllChoicesProperty = DependencyProperty.Register(
        nameof(ShowAllChoices), typeof(bool), typeof(CategoryChips), new PropertyMetadata(false));
    public bool ShowAllChoices { get => (bool)GetValue(ShowAllChoicesProperty); set => SetValue(ShowAllChoicesProperty, value); }
    public CategoryChips() => InitializeComponent();
}
