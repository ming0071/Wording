using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wording.Desktop.ViewModels;

namespace Wording.Desktop;

public static class ReviewKeyboard
{
    public static bool Handle(ReviewViewModel review, Key key, ModifierKeys modifiers, bool isRepeat,
        DependencyObject? focusedElement = null)
    {
        if (IsEditing(focusedElement)) return false;
        if (review.IsAnswerVisible && review.Reading is { } reading)
        {
            var readingCommand = (modifiers, key) switch
            {
                (ModifierKeys.Control, Key.Up) => reading.PreviousCommand,
                (ModifierKeys.Control, Key.Down) => reading.NextCommand,
                (ModifierKeys.None, Key.Escape) when reading.CancelCommand.CanExecute(null) => reading.CancelCommand,
                _ => null
            };
            if (readingCommand is not null)
            {
                if (!isRepeat) readingCommand.Execute(null);
                return true;
            }
        }
        if (modifiers != ModifierKeys.None) return false;
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
            if (element is TextBoxBase { IsReadOnly: false } or PasswordBox or ComboBoxItem ||
                element is ComboBox { IsDropDownOpen: true } or ComboBox { IsEditable: true }) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
}
