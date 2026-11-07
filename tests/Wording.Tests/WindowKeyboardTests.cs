using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Wording.Desktop;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public void WindowShortcutTogglesOnceAndPreservesTheNormalDimensions()
    {
        OffscreenWpf.Invoke(() =>
        {
            var window = new MainWindow();
            Assert.True(WindowKeyboard.Handle(window, Key.F, ModifierKeys.None, false));
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.True(WindowKeyboard.Handle(window, Key.F, ModifierKeys.None, true));
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.True(WindowKeyboard.Handle(window, Key.F, ModifierKeys.None, false));
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(1240, window.Width); Assert.Equal(850, window.Height);
            using var source = new HwndSource(new HwndSourceParameters("Wording window shortcut test") { Width = 1, Height = 1, WindowStyle = 0 });
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            window.RaiseEvent(key); Assert.True(key.Handled); Assert.Equal(WindowState.Maximized, window.WindowState);
            window.Close();
        });
    }

    [Fact]
    public void WindowShortcutLeavesTypingAndControlNavigationAlone()
    {
        OffscreenWpf.Invoke(() =>
        {
            var window = new MainWindow();
            foreach (var element in new DependencyObject[] { new TextBox(), new PasswordBox(), new RichTextBox(), new ComboBox(), new ListBoxItem(), new ToggleButton() })
                Assert.False(WindowKeyboard.Handle(window, Key.F, ModifierKeys.None, false, element));
            foreach (var modifiers in new[] { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Shift })
                Assert.False(WindowKeyboard.Handle(window, Key.F, modifiers, false));
            Assert.False(WindowKeyboard.Handle(window, Key.F1, ModifierKeys.None, false));
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(WindowKeyboard.Handle(window, Key.F, ModifierKeys.None, false, new TextBox { IsReadOnly = true }));
            Assert.Equal(WindowState.Maximized, window.WindowState);
            window.Close();
        });
    }
}
