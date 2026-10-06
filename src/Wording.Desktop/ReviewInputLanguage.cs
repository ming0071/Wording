using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wording.Core;

namespace Wording.Desktop;

public static class ReviewInputLanguage
{
    private static readonly DependencyProperty PolicyProperty = DependencyProperty.RegisterAttached("Policy", typeof(Policy),
        typeof(ReviewInputLanguage));
    public static CultureInfo? SelectTaiwanese(IEnumerable<CultureInfo> available) => available.FirstOrDefault(x =>
        string.Equals(x.Name, ApplicationConfiguration.Current.Review.PreferredInputLanguage, StringComparison.OrdinalIgnoreCase));

    public static void Configure(FrameworkElement scope) =>
        Configure(scope, InputLanguageManager.Current.AvailableInputLanguages.Cast<CultureInfo>());

    public static void Configure(FrameworkElement scope, IEnumerable<CultureInfo> available)
    {
        if (scope.GetValue(PolicyProperty) is Policy previous) previous.Detach();
        scope.ClearValue(PolicyProperty);
        var taiwanese = SelectTaiwanese(available);
        if (taiwanese is null) return;
        scope.SetValue(PolicyProperty, new Policy(scope, taiwanese));
    }

    private sealed class Policy
    {
        private readonly FrameworkElement scope;
        private readonly CultureInfo taiwanese;
        private CultureInfo? original;

        public Policy(FrameworkElement scope, CultureInfo taiwanese)
        {
            this.scope = scope;
            this.taiwanese = taiwanese;
            scope.PreviewGotKeyboardFocus += Enter;
            scope.LostKeyboardFocus += Leave;
            scope.Unloaded += Unload;
        }

        private bool Contains(IInputElement? element) => ReferenceEquals(scope, element) ||
            element is Visual visual && scope.IsAncestorOf(visual);

        private void Enter(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!Contains(e.NewFocus) || e.NewFocus is not DependencyObject target) return;
            // Capture once for the whole page, rather than on every child-button focus.
            original ??= InputLanguageManager.Current.CurrentInputLanguage;
            if (!InputLanguageManager.Current.CurrentInputLanguage.Equals(taiwanese))
                InputLanguageManager.Current.CurrentInputLanguage = taiwanese;
            InputMethod.SetIsInputMethodEnabled(target, true);
            InputMethod.SetPreferredImeState(target, InputMethodState.Off);
            InputMethod.SetPreferredImeConversionMode(target, ImeConversionModeValues.Alphanumeric);
        }

        private void Leave(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!Contains(e.NewFocus)) Restore();
        }

        private void Restore()
        {
            if (original is null) return;
            InputLanguageManager.Current.CurrentInputLanguage = original;
            original = null;
        }

        private void Unload(object sender, RoutedEventArgs e) => Restore();

        public void Detach()
        {
            Restore();
            scope.PreviewGotKeyboardFocus -= Enter;
            scope.LostKeyboardFocus -= Leave;
            scope.Unloaded -= Unload;
        }
    }
}
