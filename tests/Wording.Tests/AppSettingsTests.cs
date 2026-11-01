using System.Numerics;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void ReadingShortcutsMigrateConflictsAndRoundTripWithoutOverwritingOldKeys()
    {
        using var data = new StudyTestData();
        var initial = AppSettings.Load(data.DirectoryPath);
        Assert.Equal("Space", initial.RecordReadingKey); Assert.Equal("R", initial.ReplayReadingKey);
        initial.Validate(); // Space deliberately shares the question-side flip action.
        File.WriteAllText(Path.Combine(data.DirectoryPath, "settings.json"), "{\"GoodKey\":\"Space\",\"FlipKey\":\"F1\",\"SpeakKey\":\"R\"}");
        var migrated = AppSettings.Load(data.DirectoryPath);
        Assert.Equal("Space", migrated.GoodKey); Assert.Equal("R", migrated.SpeakKey);
        Assert.NotEqual("Space", migrated.RecordReadingKey); Assert.NotEqual("R", migrated.ReplayReadingKey);
        Assert.NotEqual(migrated.RecordReadingKey, migrated.ReplayReadingKey);
        migrated.RecordReadingKey = "F7"; migrated.ReplayReadingKey = "F8"; migrated.Save(data.DirectoryPath);
        var saved = AppSettings.Load(data.DirectoryPath);
        Assert.Equal("F7", saved.RecordReadingKey); Assert.Equal("F8", saved.ReplayReadingKey);
        saved.RecordReadingKey = saved.GoodKey; Assert.Throws<ArgumentException>(() => saved.Save(data.DirectoryPath));
        saved.RecordReadingKey = saved.ReplayReadingKey; Assert.Throws<ArgumentException>(() => saved.Validate());
        saved.RecordReadingKey = "Ctrl+R"; Assert.Throws<ArgumentException>(() => saved.Validate());
    }

    [Fact]
    public void LegacyShortcutMappingsGetUnusedSpeechKeysAndNewSpeechSettingsRoundTrip()
    {
        using var data = new StudyTestData();
        File.WriteAllText(Path.Combine(data.DirectoryPath, "settings.json"), "{\"FlipKey\":\"S\",\"GoodKey\":\"E\"}");
        var settings = AppSettings.Load(data.DirectoryPath);
        Assert.Equal("S", settings.FlipKey);
        Assert.Equal("E", settings.GoodKey);
        Assert.NotEqual("S", settings.SpeakKey);
        Assert.NotEqual("E", settings.SpeakExampleKey);
        settings.Validate();
        settings.SpeakKey = "F7";
        settings.SpeakExampleKey = "F8";
        settings.Save(data.DirectoryPath);
        var actual = AppSettings.Load(data.DirectoryPath);
        Assert.Equal("F7", actual.SpeakKey);
        Assert.Equal("F8", actual.SpeakExampleKey);
        settings.SpeakKey = settings.FlipKey;
        Assert.Throws<ArgumentException>(() => settings.Save(data.DirectoryPath));
    }

    [Fact]
    public void LargeLimitsAndCustomShortcutsRoundTripAndLegacyNumbersStillLoad()
    {
        using var data = new StudyTestData();
        File.WriteAllText(Path.Combine(data.DirectoryPath, "settings.json"), "{\"DailyNewLimit\":7}");
        var settings = AppSettings.Load(data.DirectoryPath);
        Assert.Equal(new BigInteger(7), settings.DailyNewLimit);
        Assert.Equal("Space", settings.FlipKey);
        settings.DailyNewLimit = BigInteger.Parse("99999999999999999999999999999999999");
        settings.FlipKey = "F1";
        settings.GoodKey = "J";
        settings.Save(data.DirectoryPath);
        var saved = AppSettings.Load(data.DirectoryPath);
        Assert.Equal(settings.DailyNewLimit, saved.DailyNewLimit);
        Assert.Equal("F1", saved.FlipKey);
        Assert.Equal("J", saved.GoodKey);
    }

    [Fact]
    public void InvalidSettingsCannotOverwriteExistingFile()
    {
        using var data = new StudyTestData();
        var settings = new AppSettings();
        settings.Save(data.DirectoryPath);
        var path = Path.Combine(data.DirectoryPath, "settings.json");
        var original = File.ReadAllText(path);
        settings.FlipKey = settings.AgainKey;
        Assert.Throws<ArgumentException>(() => settings.Save(data.DirectoryPath));
        Assert.Equal(original, File.ReadAllText(path));
        settings.FlipKey = "Space";
        settings.DailyNewLimit = -1;
        Assert.Throws<ArgumentException>(() => settings.Save(data.DirectoryPath));
        Assert.Equal(original, File.ReadAllText(path));
    }
}
