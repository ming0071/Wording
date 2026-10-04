using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void DefaultDirectoryUsesWordingWithOrWithoutExistingData()
    {
        using var data = new StudyTestData();
        var current = Path.Combine(data.DirectoryPath, "Wording");
        Assert.Equal(current, AppDataPaths.DefaultDirectory(data.DirectoryPath));
        Directory.CreateDirectory(Path.Combine(current, "DictionaryBrowser"));
        Assert.Equal(current, AppDataPaths.DefaultDirectory(data.DirectoryPath));
        File.WriteAllText(Path.Combine(current, "settings.json"), "existing settings");
        File.WriteAllText(Path.Combine(current, "wording.db"), "new database");
        Assert.Equal(current, AppDataPaths.DefaultDirectory(data.DirectoryPath));
        Assert.Equal(Path.Combine(current, "wording.db"), AppDataPaths.DatabasePath(current));
        Assert.Equal("existing settings", File.ReadAllText(Path.Combine(current, "settings.json")));
    }

    [Fact]
    public void CustomDataDirectoryAlwaysUsesWordingDatabase()
    {
        using var data = new StudyTestData();
        Assert.Equal(Path.Combine(data.DirectoryPath, "wording.db"), AppDataPaths.DatabasePath(data.DirectoryPath));
        File.WriteAllText(Path.Combine(data.DirectoryPath, "wording.db"), "current");
        Assert.Equal(Path.Combine(data.DirectoryPath, "wording.db"), AppDataPaths.DatabasePath(data.DirectoryPath));
        Assert.Equal("current", File.ReadAllText(AppDataPaths.DatabasePath(data.DirectoryPath)));
    }
}
