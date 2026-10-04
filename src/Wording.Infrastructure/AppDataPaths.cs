namespace Wording.Infrastructure;

public static class AppDataPaths
{
    public static string DefaultDirectory(string? localApplicationData = null)
    {
        var root = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Wording");
    }

    public static string DatabasePath(string directory) => Path.Combine(directory, "wording.db");
}
