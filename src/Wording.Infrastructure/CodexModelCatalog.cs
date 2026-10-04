using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Wording.Core;

namespace Wording.Infrastructure;

// Queries metadata only. Never starts a thread/turn, reads auth files, or returns account details.
public sealed class CodexModelCatalog(string workingDirectory) : ICodexModelCatalog
{
    public async Task<IReadOnlyList<CodexModel>> ListModelsAsync(string executable, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(workingDirectory);
        var arguments = new[] { "app-server", "--listen", "stdio://", "-c", "model_provider=\"openai\"",
            "-c", "forced_login_method=\"chatgpt\"", "-c", "approval_policy=\"never\"" };
        using var process = new Process { StartInfo = CodexProcessRunner.CreateStartInfo(executable, arguments, workingDirectory) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(ApplicationConfiguration.Current.Ai.ModelCatalogTimeoutSeconds));
        Task? stderr = null;
        var started = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start()) throw new InvalidOperationException("無法啟動 Codex 模型清單查詢。");
            started = true;
            stderr = DrainErrorAsync(process.StandardError, deadline);
            return await ReadCatalogAsync(process.StandardOutput, process.StandardInput, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (stderr?.IsFaulted == true) await stderr;
            throw new TimeoutException("模型清單查詢逾時；可稍後更新清單，或使用 CLI 預設模型。");
        }
        finally
        {
            deadline.Cancel();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync(cleanup.Token);
            }
            if (stderr is not null)
            {
                try { await stderr.WaitAsync(cleanup.Token); }
                catch (OperationCanceledException) when (stderr.IsCanceled) { }
            }
        }
    }

    private static async Task DrainErrorAsync(StreamReader reader, CancellationTokenSource deadline)
    {
        var buffer = new char[4096];
        var total = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), deadline.Token)) > 0)
        {
            total += count;
            if (total <= 262_144) continue;
            deadline.Cancel();
            throw new InvalidDataException("模型清單查詢輸出超出限制。");
        }
    }

    public static async Task<IReadOnlyList<CodexModel>> ReadCatalogAsync(TextReader input, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var lines = new BoundedJsonLines(input);
        var requestId = 0;
        async Task Send(object message)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        async Task<JsonElement> Request(string method, object parameters)
        {
            var id = requestId++;
            await Send(new { id, method, @params = parameters });
            while (true)
            {
                var line = await lines.ReadAsync(cancellationToken);
                if (line is null) throw new InvalidDataException("Codex 未回傳完整模型清單；請確認 CLI 支援 app-server 並已登入。");
                JsonDocument document;
                try { document = JsonDocument.Parse(line); }
                catch (JsonException) { throw new InvalidDataException("Codex 模型清單回應格式無效。"); }
                using (document)
                {
                    var response = document.RootElement;
                    if (response.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Codex 模型清單回應格式無效。");
                    if (response.TryGetProperty("method", out _) && response.TryGetProperty("id", out var serverId))
                    {
                        await Send(new { id = serverId.Clone(), error = new { code = -32601, message = "Unsupported client request" } });
                        continue;
                    }
                    if (!response.TryGetProperty("id", out var responseId) || !responseId.TryGetInt32(out var number) || number != id) continue;
                    if (response.TryGetProperty("error", out _))
                        throw new InvalidOperationException($"Codex 不支援或無法完成 {method}；請更新 CLI 並確認 ChatGPT 登入。仍可使用 CLI 預設模型。");
                    if (!response.TryGetProperty("result", out var result)) throw new InvalidDataException("Codex 模型清單回應缺少結果。");
                    return result.Clone();
                }
            }
        }
        await Request("initialize", new { clientInfo = new { name = "wording", title = "Wording",
            version = typeof(CodexModelCatalog).Assembly.GetName().Version?.ToString(3) ?? "" } });
        await Send(new { method = "initialized", @params = new { } });
        var account = await Request("account/read", new { refreshToken = false });
        if (!account.TryGetProperty("account", out var details) || details.ValueKind != JsonValueKind.Object ||
            !details.TryGetProperty("type", out var type) || type.GetString() != "chatgpt")
            throw new InvalidOperationException("請先使用 codex login 完成 ChatGPT 登入，再更新模型清單。");
        var models = new Dictionary<string, CodexModel>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var result = await Request("model/list", new { limit = 100, includeHidden = false, cursor });
            if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Codex 模型清單缺少模型資料。");
            foreach (var model in data.EnumerateArray())
            {
                if (model.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True) continue;
                if (model.TryGetProperty("inputModalities", out var modalities) && modalities.ValueKind == JsonValueKind.Array &&
                    !modalities.EnumerateArray().Any(x => x.GetString() == "text")) continue;
                var slug = model.TryGetProperty("model", out var name) ? name.GetString() : model.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200 || slug.Any(char.IsControl))
                    throw new InvalidDataException("Codex 模型識別碼無效。");
                var display = model.TryGetProperty("displayName", out var label) ? label.GetString() : slug;
                var description = model.TryGetProperty("description", out var text) ? text.GetString() : "";
                models.TryAdd(slug, new(slug, string.IsNullOrWhiteSpace(display) ? slug : display,
                    description ?? "", model.TryGetProperty("isDefault", out var recommended) && recommended.ValueKind == JsonValueKind.True)
                    { ServiceTiers = ReadServiceTiers(model) });
            }
            cursor = result.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            if (cursor is not null && (!cursors.Add(cursor) || cursors.Count > 10))
                throw new InvalidDataException("Codex 模型清單分頁無效。");
        } while (cursor is not null);
        if (models.Count == 0) throw new InvalidDataException("Codex 沒有回傳可用的文字模型；請更新 CLI 或使用 CLI 預設。");
        return models.Values.ToArray();
    }

    private static IReadOnlyList<CodexServiceTier> ReadServiceTiers(JsonElement model)
    {
        var tiers = new Dictionary<string, CodexServiceTier>(StringComparer.Ordinal);
        if (model.TryGetProperty("serviceTiers", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var tier in values.EnumerateArray())
            {
                var id = tier.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(id) || !CodexServiceTier.IsValidId(id))
                    throw new InvalidDataException("Codex 加速模式識別碼無效。");
                if (id == "default") continue;
                tiers.TryAdd(id, new(id, tier.TryGetProperty("name", out var name) ? name.GetString() ?? id : id,
                    tier.TryGetProperty("description", out var text) ? text.GetString() ?? "" : ""));
            }
        }
        // Older CLI catalogs advertise Fast via this deprecated field.
        else if (model.TryGetProperty("additionalSpeedTiers", out var legacy) && legacy.ValueKind == JsonValueKind.Array &&
                 legacy.EnumerateArray().Any(x => x.GetString() == "fast"))
            tiers.Add("fast", new("fast", "Fast", "由 Codex 提供的加速模式；速度與額度消耗依模型而異。"));
        return tiers.Values.ToArray();
    }

    private sealed class BoundedJsonLines(TextReader reader)
    {
        private readonly char[] buffer = new char[4096];
        private int position, count, total;
        public async Task<string?> ReadAsync(CancellationToken token)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (position == count)
                {
                    count = await reader.ReadAsync(buffer.AsMemory(), token);
                    position = 0;
                    total += count;
                    if (total > 262_144) throw new InvalidDataException("模型清單查詢輸出超出限制。");
                    if (count == 0) return line.Length == 0 ? null : line.ToString();
                }
                var newline = Array.IndexOf(buffer, '\n', position, count - position);
                var end = newline < 0 ? count : newline;
                line.Append(buffer, position, end - position);
                position = newline < 0 ? count : end + 1;
                if (newline >= 0) return line.ToString().TrimEnd('\r');
            }
        }
    }
}
