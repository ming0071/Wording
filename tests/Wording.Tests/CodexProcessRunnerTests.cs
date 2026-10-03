using System.Diagnostics;
using System.Text;
using Wording.Infrastructure;

namespace Wording.Tests;

/// <summary>Real local processes only: these tests never start Codex or make network requests.</summary>
public sealed class CodexProcessRunnerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "WordingProcessTests", Guid.NewGuid().ToString("N"));
    private readonly CodexProcessRunner runner = new();
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    public CodexProcessRunnerTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task LargeStdoutAndStderrAreDrainedTogetherAndPreserveUtf8()
    {
        const string script = """
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            [Console]::Out.Write('繁體中文：')
            [Console]::Error.Write('錯誤管線：')
            $outChunk = 'o' * 2048
            $errChunk = 'e' * 2048
            for ($i = 0; $i -lt 80; $i++) {
                [Console]::Out.Write($outChunk)
                [Console]::Error.Write($errChunk)
            }
            """;
        var result = await RunScriptAsync(script, TimeSpan.FromSeconds(20)).WaitAsync(TimeSpan.FromSeconds(28));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("繁體中文：" + new string('o', 2048 * 80), result.StandardOutput);
        Assert.Equal("錯誤管線：" + new string('e', 2048 * 80), result.StandardError);
    }

    [Fact]
    public async Task StandardInputPreservesUtf8AndNonzeroExitCodeIsReturned()
    {
        const string script = """
            [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            [Console]::Out.Write([Console]::In.ReadToEnd())
            exit 7
            """;
        const string prompt = "繁體中文 prompt：invoice\n第二行";
        var result = await runner.RunAsync(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script)],
            prompt, directory, TimeSpan.FromSeconds(20), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(28));
        Assert.Equal(7, result.ExitCode);
        Assert.Equal(prompt, result.StandardOutput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputOverTheLimitStopsTheProcessWithoutWaitingForItsNormalExit(bool useStandardError)
    {
        var stream = useStandardError ? "Error" : "Out";
        var script = $$"""
            $chunk = 'x' * 4096
            for ($i = 0; $i -lt 100; $i++) { [Console]::{{stream}}.Write($chunk) }
            Start-Sleep -Seconds 120
            """;
        var elapsed = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await RunScriptAsync(script, TimeSpan.FromSeconds(60)).WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Contains("輸出超出限制", error.Message);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task TimeoutTerminatesARealProcessAndReturnsTheTimeoutFailure()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await RunScriptAsync("Start-Sleep -Seconds 120", TimeSpan.FromMilliseconds(750))
                .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("Codex 回應逾時", error.Message);
    }

    [Fact]
    public async Task CancellationTerminatesOnlyTheStartedParentAndItsChild()
    {
        var pidFile = Path.Combine(directory, "started-processes.txt");
        var childCommand = Encode("Start-Sleep -Seconds 120");
        var script = $$"""
            $info = [Diagnostics.ProcessStartInfo]::new()
            $info.FileName = '{{PowerShell.Replace("'", "''")}}'
            $info.Arguments = '-NoLogo -NoProfile -NonInteractive -EncodedCommand {{childCommand}}'
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            $child = [Diagnostics.Process]::Start($info)
            [IO.File]::WriteAllText('{{pidFile.Replace("'", "''")}}', "$PID,$($child.Id)")
            Start-Sleep -Seconds 120
            """;
        using var cancel = new CancellationTokenSource();
        var running = RunScriptAsync(script, TimeSpan.FromSeconds(60), cancel.Token);
        Process? parent = null;
        Process? child = null;
        try
        {
            var pids = await ReadStartedProcessesAsync(pidFile, running);
            parent = Process.GetProcessById(pids[0]);
            child = Process.GetProcessById(pids[1]);
            // Hold handles to these exact processes; cleanup must never target a reused PID.
            _ = parent.Handle;
            _ = child.Handle;
            Assert.False(parent.HasExited);
            Assert.False(child.HasExited);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await running.WaitAsync(TimeSpan.FromSeconds(10)));
            await Task.WhenAll(parent.WaitForExitAsync(), child.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(parent.HasExited);
            Assert.True(child.HasExited);
        }
        finally
        {
            cancel.Cancel();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception) { /* The assertions above verify the expected result. */ }
            StopOwnedProcess(child);
            StopOwnedProcess(parent);
        }
    }

    [Fact]
    public async Task AlreadyCanceledRequestDoesNotTryToStartAnExecutable()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            "this-executable-must-not-be-started.exe", [], "", directory,
            TimeSpan.FromSeconds(10), cancel.Token));
    }

    private Task<CodexRunResult> RunScriptAsync(string script, TimeSpan timeout, CancellationToken token = default) =>
        runner.RunAsync(PowerShell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script)],
            "", directory, timeout, token);

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static async Task<int[]> ReadStartedProcessesAsync(string path, Task<CodexRunResult> running)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (true)
        {
            limit.Token.ThrowIfCancellationRequested();
            if (running.IsCompleted)
                throw new InvalidOperationException("The fixture exited before starting its child: " + (await running).StandardError);
            if (File.Exists(path))
            {
                var parts = (await File.ReadAllTextAsync(path, limit.Token)).Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out var parent) && int.TryParse(parts[1], out var child))
                    return [parent, child];
            }
            await Task.Delay(50, limit.Token);
        }
    }

    private static void StopOwnedProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000)) throw new InvalidOperationException("A test-owned process did not stop.");
            }
        }
        finally { process.Dispose(); }
    }

    public void Dispose()
    {
        var pidFile = Path.Combine(directory, "started-processes.txt");
        if (File.Exists(pidFile)) File.Delete(pidFile);
        // Windows can release the exited process's current-directory handle slightly later
        // than its exit event. Bound the retry and still fail if the directory remains locked.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(directory); // Unique per-test directory; no recursive deletion.
                break;
            }
            catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
        }
    }
}
