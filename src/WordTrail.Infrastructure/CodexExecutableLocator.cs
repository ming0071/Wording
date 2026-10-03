namespace WordTrail.Infrastructure;

public static class CodexExecutableLocator
{
    public static string Resolve(string configuredPath)
    {
        var value = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
        if (value.Length == 0) value = "codex";
        if (Path.IsPathRooted(value) || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(value)) return Path.GetFullPath(value);
            throw new FileNotFoundException("找不到設定的 Codex 執行檔，請在設定頁按「自動尋找」或重新選擇檔案。", value);
        }
        // Other executables are used by process-runner clients and should retain normal PATH lookup.
        if (!value.Equals("codex", StringComparison.OrdinalIgnoreCase) && !value.Equals("codex.exe", StringComparison.OrdinalIgnoreCase))
            return value;
        var paths = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            .SelectMany(target => (Environment.GetEnvironmentVariable("PATH", target) ?? "").Split(Path.PathSeparator))
            .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in paths)
        {
            var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')), "codex.exe");
            if (File.Exists(candidate)) return candidate;
        }
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        var installed = FindDesktopExecutable(installRoot);
        if (installed is not null) return installed;
        // npm's shim is a .cmd file; launch its native executable directly without a shell.
        var npmRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
        if (Directory.Exists(npmRoot))
        {
            var native = Directory.EnumerateFiles(npmRoot, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (native is not null) return native;
        }
        throw new FileNotFoundException("找不到 Codex 執行檔。請先安裝 Codex，或在設定頁選擇 codex.exe。");
    }

    public static string? FindDesktopExecutable(string installRoot) => !Directory.Exists(installRoot) ? null :
        Directory.EnumerateDirectories(installRoot).Select(path => Path.Combine(path, "codex.exe"))
            .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
}
