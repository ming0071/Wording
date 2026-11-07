using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Wording.Desktop;

public static class WindowKeyboard
{
    public static bool Handle(Window window, Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? focusedElement = null)
    {
        if (key != Key.F || modifiers != ModifierKeys.None || ReviewKeyboard.IsEditing(focusedElement) || IsSelecting(focusedElement)) return false;
        if (!isRepeat) window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        return true;
    }

    private static bool IsSelecting(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Selector or ListBoxItem) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
}
