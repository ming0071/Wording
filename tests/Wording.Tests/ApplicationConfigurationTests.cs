using System.Numerics;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Input;
using System.Globalization;
using System.Windows;
using System.Collections;
using System.Reflection;
using Wording.Core;
using Wording.Desktop;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class ApplicationConfigurationTests
{
    private static JsonObject Defaults() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, ApplicationConfiguration.RelativePath)))!.AsObject();

    [Fact]
    public void DefaultsApplyToMissingSettingsWithoutOverwritingSavedChoices()
    {
        using var data = new StudyTestData();
        Assert.Equal(new BigInteger(25), AppSettings.Load(data.DirectoryPath).DailyNewLimit);
        File.WriteAllText(Path.Combine(data.DirectoryPath, "settings.json"), "{\"DailyNewLimit\":5}");
        Assert.Equal(new BigInteger(5), AppSettings.Load(data.DirectoryPath).DailyNewLimit);
        var options = new PracticeOptions();
        Assert.Equal(PracticeLevel.Medium, options.Level);
        Assert.Equal(WordDensity.Medium, options.Density);
        Assert.Equal(CodexContentGenerator.WordRange(PracticeLength.Short).Minimum,
            ApplicationConfiguration.Current.Practice.Lengths[PracticeLength.Short].MinimumWords);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("negative")]
    [InlineData("range")]
    [InlineData("shortcut")]
    [InlineData("language")]
    [InlineData("null")]
    public void InvalidProductConfigurationFailsClearlyBeforeUse(string invalid)
    {
        var json = Defaults();
        switch (invalid)
        {
            case "missing": json.Remove("Review"); break;
            case "unknown": json["Typo"] = 1; break;
            case "negative": json["Review"]!["DailyNewLimit"] = "-1"; break;
            case "range": json["Practice"]!["Lengths"]!["Short"]!["MaximumWords"] = 1; break;
            case "shortcut": json["Shortcuts"]!["Flip"] = "1"; break;
            case "language": json["Review"]!["PreferredInputLanguage"] = "en-US"; break;
            case "null": json["Ai"] = null; break;
        }
        var error = Assert.Throws<InvalidDataException>(() => ApplicationConfiguration.Parse(json.ToJsonString()));
        Assert.Contains(ApplicationConfiguration.RelativePath, error.Message);
    }

    [Fact]
    public void CustomAdaptiveThresholdsPreserveBoundaryMeaning()
    {
        var json = Defaults();
        json["Review"]!["ReduceNewAtDueCount"] = 12;
        json["Review"]!["PauseNewAtDueCount"] = 24;
        json["Review"]!["ReducedNewLimit"] = 4;
        var config = ApplicationConfiguration.Parse(json.ToJsonString());
        Assert.Null(config.Review.AdaptiveNewLimit(11));
        Assert.Equal(4, config.Review.AdaptiveNewLimit(12));
        Assert.Equal(4, config.Review.AdaptiveNewLimit(23));
        Assert.Equal(0, config.Review.AdaptiveNewLimit(24));
    }

    [Fact]
    public void ReviewSelectsOnlyInstalledTaiwaneseInputLanguage()
    {
        var chinese = CultureInfo.GetCultureInfo("zh-TW");
        var uk = CultureInfo.GetCultureInfo("en-GB");
        var us = CultureInfo.GetCultureInfo("en-US");
        Assert.Same(chinese, ReviewInputLanguage.SelectTaiwanese([us, uk, chinese]));
        Assert.Same(chinese, ReviewInputLanguage.SelectTaiwanese([chinese]));
        Assert.Null(ReviewInputLanguage.SelectTaiwanese([us, uk, CultureInfo.GetCultureInfo("zh-CN")]));
        Assert.Null(ReviewInputLanguage.SelectTaiwanese([]));
    }

    [Fact]
    public void ReviewImePolicyIsScopedAndRestoresPreviousInputLanguage()
    {
        OffscreenWpf.Invoke(() =>
        {
            var manager = InputLanguageManager.Current;
            var originalSource = (IInputLanguageSource)typeof(InputLanguageManager).GetProperty("Source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            var source = new TestInputLanguageSource();
            manager.RegisterInputLanguageSource(source);
            try
            {
                var review = new StackPanel();
                var child = new Button();
                review.Children.Add(child);
                ReviewInputLanguage.Configure(review, [CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("zh-TW")]);
                RaiseFocus(review, Keyboard.PreviewGotKeyboardFocusEvent, null, review);
                Assert.Equal("zh-TW", manager.CurrentInputLanguage.Name);
                Assert.Equal(InputMethodState.Off, InputMethod.GetPreferredImeState(review));
                RaiseFocus(child, Keyboard.PreviewGotKeyboardFocusEvent, review, child);
                RaiseFocus(review, Keyboard.LostKeyboardFocusEvent, review, child);
                Assert.Equal("zh-TW", manager.CurrentInputLanguage.Name);
                Assert.True(InputMethod.GetIsInputMethodEnabled(review));
                Assert.True(InputMethod.GetIsInputMethodEnabled(child));
                Assert.Equal(InputMethodState.Off, InputMethod.GetPreferredImeState(child));
                Assert.Equal(ImeConversionModeValues.Alphanumeric, InputMethod.GetPreferredImeConversionMode(child));
                var editor = new TextBox();
                RaiseFocus(child, Keyboard.LostKeyboardFocusEvent, child, editor);
                Assert.Equal("en-US", manager.CurrentInputLanguage.Name);
                Assert.True(InputMethod.GetIsInputMethodEnabled(editor));
                Assert.Equal(InputMethodState.DoNotCare, InputMethod.GetPreferredImeState(editor));
                RaiseFocus(review, Keyboard.PreviewGotKeyboardFocusEvent, editor, review);
                Assert.Equal("zh-TW", manager.CurrentInputLanguage.Name);
                review.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.Equal("en-US", manager.CurrentInputLanguage.Name);
            }
            finally { manager.RegisterInputLanguageSource(originalSource); }
        });
    }

    [Fact]
    public void MissingTaiwaneseInputLanguageLeavesExistingPolicyUntouched()
    {
        OffscreenWpf.Invoke(() =>
        {
            var review = new StackPanel();
            InputLanguageManager.SetInputLanguage(review, CultureInfo.GetCultureInfo("ja-JP"));
            InputMethod.SetPreferredImeState(review, InputMethodState.On);
            InputMethod.SetPreferredImeConversionMode(review, ImeConversionModeValues.Native);
            ReviewInputLanguage.Configure(review, [CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("ja-JP")]);
            RaiseFocus(review, Keyboard.PreviewGotKeyboardFocusEvent, null, review);
            Assert.Equal("ja-JP", InputLanguageManager.GetInputLanguage(review).Name);
            Assert.Equal(InputMethodState.On, InputMethod.GetPreferredImeState(review));
            Assert.Equal(ImeConversionModeValues.Native, InputMethod.GetPreferredImeConversionMode(review));
            Assert.False(InputLanguageManager.GetRestoreInputLanguage(review));
        });
    }

    private static void RaiseFocus(UIElement target, RoutedEvent routedEvent, IInputElement? oldFocus, IInputElement? newFocus) =>
        target.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, oldFocus, newFocus) { RoutedEvent = routedEvent });

    private sealed class TestInputLanguageSource : IInputLanguageSource
    {
        public CultureInfo CurrentInputLanguage { get; set; } = CultureInfo.GetCultureInfo("en-US");
        public IEnumerable InputLanguageList => new[] { CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("zh-TW") };
        public void Initialize() { }
        public void Uninitialize() { }
    }
}
