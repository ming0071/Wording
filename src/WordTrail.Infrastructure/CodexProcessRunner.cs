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
        var start = new ProcessStartInfo(CodexExecutableLocator.Resolve(executable))
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
        var started = false;
        var outputLimitExceeded = 0;
        Task<string>? stdout = null;
        Task<string>? stderr = null;
        Exception? operationFailure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start()) throw new InvalidOperationException("無法啟動 Codex。");
            started = true;
            void StopForOutputLimit()
            {
                Interlocked.Exchange(ref outputLimitExceeded, 1);
                deadline.Cancel();
            }
            stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token, StopForOutputLimit);
            stderr = ReadBoundedAsync(process.StandardError, deadline.Token, StopForOutputLimit);
            await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            // Drain both pipes concurrently: waiting for exit first can deadlock when a pipe fills.
            var exited = process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdout, stderr, exited);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException exception) when (Volatile.Read(ref outputLimitExceeded) != 0)
        {
            operationFailure = new InvalidDataException("Codex 輸出超出限制，已停止此次生成。", exception);
            throw operationFailure;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            operationFailure = new TimeoutException("Codex 回應逾時。此次不會自動重試，你可以稍後再試。", exception);
            throw operationFailure;
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            throw;
        }
        finally
        {
            // This separate deadline also covers draining the canceled readers. The request
            // deadline is already canceled here, so reusing it cannot verify process exit.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            deadline.Cancel();
            try
            {
                if (started && !process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { /* Exit raced with Kill. */ }
                    await process.WaitForExitAsync(cleanup.Token);
                }
                if (stdout is not null && stderr is not null)
                {
                    try { await Task.WhenAll(stdout, stderr).WaitAsync(cleanup.Token); }
                    catch (Exception) when (!cleanup.IsCancellationRequested)
                    {
                        // Observe both readers' cancellation/faults. The original failure is
                        // already being propagated by the request, not reported as success.
                    }
                }
            }
            catch (Exception cleanupFailure)
            {
                throw new InvalidOperationException(
                    "Codex 清理未完成：未能在清理期限內確認子程序終止或輸出關閉。請確認該次程序狀態後再試。",
                    operationFailure is null ? cleanupFailure : new AggregateException(operationFailure, cleanupFailure));
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken,
        Action stopForOutputLimit)
    {
        const int maximumCharacters = 262_144;
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (result.Length + count > maximumCharacters)
            {
                stopForOutputLimit();
                throw new InvalidDataException("Codex 輸出超出限制，已停止此次生成。");
            }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
