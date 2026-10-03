using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void FreshInstallUsesWordingAndExistingUsersKeepTheirDatabase()
    {
        using var data = new StudyTestData();
        var current = Path.Combine(data.DirectoryPath, "Wording");
        var legacy = Path.Combine(data.DirectoryPath, "WordTrail");
        Assert.Equal(current, AppDataPaths.DefaultDirectory(data.DirectoryPath));
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "wordtrail.db"), "existing database");
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "existing settings");
        // A browser cache alone must not hide the existing vocabulary library.
        Directory.CreateDirectory(Path.Combine(current, "DictionaryBrowser"));
        Assert.Equal(legacy, AppDataPaths.DefaultDirectory(data.DirectoryPath));
        Assert.Equal(Path.Combine(legacy, "wordtrail.db"), AppDataPaths.DatabasePath(legacy));
        Assert.Equal("existing settings", File.ReadAllText(Path.Combine(legacy, "settings.json")));
        File.WriteAllText(Path.Combine(current, "wording.db"), "new database");
        Assert.Equal(current, AppDataPaths.DefaultDirectory(data.DirectoryPath));
    }

    [Fact]
    public void CustomDataDirectoryAlsoRecognizesLegacyDatabaseWithoutMovingFiles()
    {
        using var data = new StudyTestData();
        Assert.Equal(Path.Combine(data.DirectoryPath, "wording.db"), AppDataPaths.DatabasePath(data.DirectoryPath));
        var legacy = Path.Combine(data.DirectoryPath, "wordtrail.db");
        File.WriteAllText(legacy, "legacy");
        Assert.Equal(legacy, AppDataPaths.DatabasePath(data.DirectoryPath));
        File.WriteAllText(Path.Combine(data.DirectoryPath, "wording.db"), "current");
        Assert.Equal(Path.Combine(data.DirectoryPath, "wording.db"), AppDataPaths.DatabasePath(data.DirectoryPath));
        Assert.Equal("legacy", File.ReadAllText(legacy));
    }
}
