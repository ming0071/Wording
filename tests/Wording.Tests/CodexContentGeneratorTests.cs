using System.Text.Json;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class CodexContentGeneratorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "WordingTests", Guid.NewGuid().ToString("N"));
    private static readonly Guid SenseId = Guid.Parse("790386aa-e927-4f26-9409-a8875f24c9e7");

    [Fact]
    public void CompletedResponsePreservesTraditionalChineseAndSense()
    {
        var result = CodexContentGenerator.ParseResponse(Response(SenseId), SenseId, "", DateTimeOffset.UtcNow);
        Assert.Equal("發票；帳單", result.Meaning);
        Assert.Equal(2, result.Examples.Length);
        Assert.Equal("ai", result.Origin.Kind);
    }

    [Fact]
    public void PartialResponseIsNotAccepted()
    {
        var response = Response(SenseId).Split('\n')[0];
        Assert.Throws<InvalidDataException>(() => CodexContentGenerator.ParseResponse(response, SenseId, "", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DifferentSenseIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => CodexContentGenerator.ParseResponse(Response(Guid.NewGuid()), SenseId, "", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("command_execution")]
    [InlineData("mcp_tool_call")]
    [InlineData("web_search")]
    [InlineData("file_change")]
    [InlineData("view_image")]
    [InlineData("unknown_future_tool")]
    public void UnexpectedToolActivityInvalidatesContent(string toolType)
    {
        var tool = JsonSerializer.Serialize(new { type = "item.completed", item = new { type = toolType } });
        Assert.Throws<InvalidDataException>(() => CodexContentGenerator.ParseResponse(tool + "\n" + Response(SenseId), SenseId, "", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MissingExampleIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => CodexContentGenerator.ParseResponse(Response(SenseId, 1), SenseId, "", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DuplicateExamplesAreRejected()
    {
        var response = Response(SenseId).Replace("The invoice includes the delivery fee.", "Please send the invoice today.");
        Assert.Throws<InvalidDataException>(() => CodexContentGenerator.ParseResponse(response, SenseId, "", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FailureSummaryDoesNotExposeRawDiagnostics()
    {
        var result = CodexContentGenerator.DescribeFailure("unknown configuration field; secret-from-child-output");
        Assert.Contains("不支援必要參數", result);
        Assert.DoesNotContain("secret-from-child-output", result);
    }

    [Fact]
    public async Task DailyLimitPersistsAcrossGeneratorInstances()
    {
        var settings = new AiSettings { DailyGenerationLimit = 1 };
        var runner = new StubRunner(Response(SenseId));
        await new CodexContentGenerator(settings, directory, runner).GenerateAsync(Word());
        var second = new CodexContentGenerator(settings, directory, runner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.GenerateAsync(Word()));
        Assert.Equal(1, runner.GenerationCount);
    }

    [Fact]
    public async Task FailedGenerationDoesNotRetryOrSwitchProvider()
    {
        var runner = new StubRunner("", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CodexContentGenerator(new(), directory, runner).GenerateAsync(Word()));
        Assert.Equal(1, runner.GenerationCount);
        Assert.Contains("forced_login_method=\"chatgpt\"", runner.LastArguments!);
    }

    [Fact]
    public async Task ApiKeyLoginIsRejectedBeforeGeneration()
    {
        var runner = new StubRunner(Response(SenseId)) { LoginMessage = "Logged in using an API key" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CodexContentGenerator(new(), directory, runner).GenerateAsync(Word()));
        Assert.Equal(0, runner.GenerationCount);
    }

    [Fact]
    public async Task CancellationDoesNotReturnLateContent()
    {
        using var cancel = new CancellationTokenSource();
        var runner = new StubRunner(Response(SenseId)) { BeforeResult = () => cancel.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CodexContentGenerator(new(), directory, runner).GenerateAsync(Word(), cancel.Token));
    }

    private static VocabularyItem Word() => new() { Id = SenseId, Headword = "invoice", Meaning = "發票；帳單" };

    [Theory]
    [InlineData("", "default")]
    [InlineData("priority", "priority")]
    [InlineData("future_speed", "future_speed")]
    public async Task WordGenerationUsesSelectedServiceTierWithoutChangingModelOrSafety(string selected, string expected)
    {
        var runner = new StubRunner(Response(SenseId));
        await new CodexContentGenerator(new() { Model = "future-model", ServiceTier = selected }, directory, runner).GenerateAsync(Word());
        Assert.Contains($"service_tier=\"{expected}\"", runner.LastArguments!);
        Assert.Contains("future-model", runner.LastArguments!);
        Assert.Contains("fast_mode", runner.LastArguments!);
        Assert.Contains("--ignore-user-config", runner.LastArguments!);
        Assert.Contains("forced_login_method=\"chatgpt\"", runner.LastArguments!);
    }

    private static string Response(Guid senseId, int exampleCount = 2)
    {
        var payload = JsonSerializer.Serialize(new {
            senseId, meaning = "發票；帳單", collocations = new[] { "send an invoice", "pay an invoice" },
            examples = Enumerable.Range(0, exampleCount).Select(index => new {
                english = index == 0 ? "Please send the invoice today." : "The invoice includes the delivery fee.",
                chinese = index == 0 ? "請今天寄送帳單。" : "帳單包含運費。" })
        });
        return JsonSerializer.Serialize(new { type = "item.completed", item = new { type = "agent_message", text = payload } }) +
            "\n" + JsonSerializer.Serialize(new { type = "turn.completed" });
    }

    private sealed class StubRunner(string response, int exitCode = 0) : ICodexProcessRunner
    {
        public string LoginMessage { get; init; } = "Logged in using ChatGPT";
        public int GenerationCount { get; private set; }
        public IReadOnlyList<string>? LastArguments { get; private set; }
        public Action? BeforeResult { get; init; }

        public Task<CodexRunResult> RunAsync(string executable, IReadOnlyList<string> arguments,
            string input, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (arguments[0] == "--version") return Task.FromResult(new CodexRunResult(0, "codex-cli 0.153.4", ""));
            if (arguments[0] == "login") return Task.FromResult(new CodexRunResult(0, "", LoginMessage));
            GenerationCount++;
            LastArguments = arguments;
            BeforeResult?.Invoke();
            return Task.FromResult(new CodexRunResult(exitCode, response, ""));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
