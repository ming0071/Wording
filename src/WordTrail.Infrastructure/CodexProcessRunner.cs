using System.Diagnostics;
using System.Text;

namespace WordTrail.Infrastructure;

public sealed record CodexRunResult(int ExitCode, string StandardOutput, string StandardError);

public interface ICodexProcessRunner
{
    Task<CodexRunResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string input, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Owns one CLI process. It never invokes a shell or accesses Codex credentials directly.</summary>
public sealed class CodexProcessRunner : ICodexProcessRunner
{
    public async Task<CodexRunResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string input, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Remove("OPENAI_API_KEY");
        start.Environment.Remove("CODEX_API_KEY");
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("無法啟動 Codex。");
            var stdout = ReadBoundedAsync(process.StandardOutput, deadline);
            var stderr = ReadBoundedAsync(process.StandardError, deadline);
            await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            // Drain both pipes concurrently: waiting for exit first can deadlock when a pipe fills.
            var exited = process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdout, stderr, exited);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Codex 回應逾時。此次不會自動重試，你可以稍後再試。");
        }
        finally
        {
            try
            {
                if (process.Id != 0 && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (InvalidOperationException) { /* Process never started or already exited. */ }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationTokenSource deadline)
    {
        const int maximumCharacters = 262_144;
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), deadline.Token)) != 0)
        {
            if (result.Length + count > maximumCharacters)
            {
                deadline.Cancel();
                throw new InvalidDataException("Codex 輸出超出限制，已停止此次生成。");
            }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
