using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Wording.Core;

namespace Wording.Desktop;

public static class ReviewInputLanguage
{
    public static CultureInfo? SelectEnglish(IEnumerable<CultureInfo> available, string preferred)
    {
        var english = available.Where(x => x.TwoLetterISOLanguageName == "en").ToArray();
        return english.FirstOrDefault(x => string.Equals(x.Name, preferred, StringComparison.OrdinalIgnoreCase))
            ?? english.FirstOrDefault();
    }

    public static void Configure(DependencyObject scope)
    {
        // Scope this to the review page; restore the previous input language when focus leaves it.
        InputMethod.SetIsInputMethodEnabled(scope, false);
        InputLanguageManager.SetRestoreInputLanguage(scope, true);
        var english = SelectEnglish(InputLanguageManager.Current.AvailableInputLanguages.Cast<CultureInfo>(),
            ApplicationConfiguration.Current.Review.PreferredInputLanguage);
        if (english is not null) InputLanguageManager.SetInputLanguage(scope, english);
    }
}
