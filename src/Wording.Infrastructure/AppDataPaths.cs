namespace Wording.Infrastructure;

public static class AppDataPaths
{
    public static string DefaultDirectory(string? localApplicationData = null)
    {
        var root = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(root, "Wording");
        var legacy = Path.Combine(root, "WordTrail");
        // Reuse the existing database in place, including its WAL and settings.
        if (File.Exists(Path.Combine(current, "wording.db"))) return current;
        if (File.Exists(Path.Combine(legacy, "wordtrail.db"))) return legacy;
        return current;
    }

    public static string DatabasePath(string directory)
    {
        var current = Path.Combine(directory, "wording.db");
        var legacy = Path.Combine(directory, "wordtrail.db");
        return !File.Exists(current) && File.Exists(legacy) ? legacy : current;
    }
}
