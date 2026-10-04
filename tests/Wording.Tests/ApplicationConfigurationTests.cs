using System.Numerics;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Input;
using System.Globalization;
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
            case "language": json["Review"]!["PreferredInputLanguage"] = "zh-TW"; break;
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
    public void EnglishSelectionUsesInstalledPreferredLanguageThenOtherEnglish()
    {
        var chinese = CultureInfo.GetCultureInfo("zh-TW");
        var uk = CultureInfo.GetCultureInfo("en-GB");
        var us = CultureInfo.GetCultureInfo("en-US");
        Assert.Same(us, ReviewInputLanguage.SelectEnglish([chinese, uk, us], "en-US"));
        Assert.Same(uk, ReviewInputLanguage.SelectEnglish([chinese, uk], "en-US"));
        Assert.Null(ReviewInputLanguage.SelectEnglish([chinese], "en-US"));
    }

    [Fact]
    public void ReviewImePolicyIsScopedAndRestoresPreviousInputLanguage()
    {
        OffscreenWpf.Invoke(() =>
        {
            var review = new StackPanel();
            var child = new Button();
            review.Children.Add(child);
            ReviewInputLanguage.Configure(review);
            Assert.False(InputMethod.GetIsInputMethodEnabled(review));
            Assert.False(InputMethod.GetIsInputMethodEnabled(child));
            Assert.True(InputLanguageManager.GetRestoreInputLanguage(review));
            Assert.True(InputMethod.GetIsInputMethodEnabled(new TextBox()));
        });
    }
}
