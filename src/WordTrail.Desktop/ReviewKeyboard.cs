using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WordTrail.Desktop.ViewModels;

namespace WordTrail.Desktop;

public static class ReviewKeyboard
{
    public static bool Handle(ReviewViewModel review, Key key, ModifierKeys modifiers, bool isRepeat,
        DependencyObject? focusedElement = null)
    {
        if (modifiers != ModifierKeys.None || IsEditing(focusedElement)) return false;
        var shortcut = key switch
        {
            Key.Return => "Enter",
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(),
            _ => key.ToString()
        };
        var command = review.CommandForShortcut(shortcut);
        if (command is null) return false;
        // Consume held/disabled shortcuts too, so a focused button cannot perform a different action.
        if (!isRepeat && command.CanExecute(null)) command.Execute(null);
        return true;
    }

    private static bool IsEditing(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or PasswordBox or ComboBoxItem ||
                element is ComboBox { IsDropDownOpen: true } or ComboBox { IsEditable: true }) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
}
