using System.Text.Json;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class CodexModelCatalogTests
{
    private const string Handshake = """
        {"id":0,"result":{"userAgent":"test"}}
        {"method":"account/updated","params":{"authMode":"chatgpt"}}
        {"id":1,"result":{"account":{"type":"chatgpt","email":"private@example.test"}}}

        """;
    private static string[] Methods(StringWriter requests) => requests.ToString()
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("method").GetString()!;
        }).ToArray();

    [Fact]
    public async Task ServiceTiersFollowCatalogIncludingFutureModesAndLegacyFast()
    {
        var responses = Handshake + """
            {"id":2,"result":{"data":[
              {"model":"future-model","serviceTiers":[{"id":"priority","name":"Fast","description":"2x speed"},{"id":"future_speed","name":"Future speed","description":"New option"}]},
              {"model":"legacy-model","additionalSpeedTiers":["fast"]},
              {"model":"standard-only","serviceTiers":[]}],"nextCursor":null}}
            """;
        using var response = JsonDocument.Parse(responses[Handshake.Length..]);
        var models = await CodexModelCatalog.ReadCatalogAsync(new StringReader(Handshake + JsonSerializer.Serialize(response.RootElement)), new StringWriter());
        Assert.Equal(new[] { "priority", "future_speed" }, models[0].ServiceTiers.Select(x => x.Id));
        Assert.Equal("2x speed", models[0].ServiceTiers[0].Description);
        Assert.Equal("fast", Assert.Single(models[1].ServiceTiers).Id);
        Assert.Empty(models[2].ServiceTiers);
    }

    [Fact]
    public async Task InvalidTierIdentifierCannotBecomeCliConfiguration()
    {
        var response = Handshake + """{"id":2,"result":{"data":[{"model":"test","serviceTiers":[{"id":"bad\"\nvalue"}]}]}}""";
        await Assert.ThrowsAsync<InvalidDataException>(() => CodexModelCatalog.ReadCatalogAsync(new StringReader(response), new StringWriter()));
    }

    [Fact]
    public async Task HandshakePaginationAndFutureModelsUseReturnedSlugsAndNames()
    {
        using var firstPage = JsonDocument.Parse("""
            {"id":2,"result":{"data":[
              {"id":"catalog-id","model":"future-text-model","displayName":"Future Text Model","isDefault":true,"inputModalities":["text"],"newField":123},
              {"model":"hidden-model","hidden":true},
              {"model":"audio-only","inputModalities":["audio"]}],"nextCursor":"next"}}
            """);
        var responses = Handshake + JsonSerializer.Serialize(firstPage.RootElement) + "\n" + """
            {"id":3,"result":{"data":[{"model":"future-small-model","description":"Small model"},{"model":"future-text-model"}],"nextCursor":null}}
            """;
        using var output = new StringWriter();
        var models = await CodexModelCatalog.ReadCatalogAsync(new StringReader(responses), output);
        Assert.Equal(new[] { "future-text-model", "future-small-model" }, models.Select(x => x.Model));
        Assert.Equal("Future Text Model", models[0].DisplayName);
        Assert.True(models[0].IsDefault);
        Assert.Equal("Small model", models[1].Description);
        Assert.Equal(new[] { "initialize", "initialized", "account/read", "model/list", "model/list" }, Methods(output));
        var requests = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var secondPage = JsonDocument.Parse(requests[^1]);
        Assert.Equal("next", secondPage.RootElement.GetProperty("params").GetProperty("cursor").GetString());
        Assert.False(secondPage.RootElement.GetProperty("params").GetProperty("includeHidden").GetBoolean());
    }

    [Fact]
    public async Task ApiKeyLoginNeverQueriesCatalogOrExposesAccountDetails()
    {
        var responses = Handshake.Replace("\"type\":\"chatgpt\"", "\"type\":\"apiKey\"");
        using var output = new StringWriter();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CodexModelCatalog.ReadCatalogAsync(new StringReader(responses), output));
        Assert.Contains("ChatGPT", error.Message);
        Assert.DoesNotContain("private@example", error.Message);
        Assert.DoesNotContain("model/list", Methods(output));
    }

    [Theory]
    [InlineData("{\"id\":2,\"result\":{\"data\":[],\"nextCursor\":null}}")]
    [InlineData("{\"id\":2,\"result\":{\"wrong\":[]}}")]
    [InlineData("broken json")]
    [InlineData("")]
    public async Task EmptyBrokenOrTruncatedCatalogIsNotReportedAsSuccess(string response)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CodexModelCatalog.ReadCatalogAsync(new StringReader(Handshake + response), new StringWriter()));
    }

    [Fact]
    public async Task ProtocolErrorsDoNotDisplayRawChildOutput()
    {
        var response = Handshake + """{"id":2,"error":{"code":-32601,"message":"private-token-from-child"}}""";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CodexModelCatalog.ReadCatalogAsync(new StringReader(response), new StringWriter()));
        Assert.Contains("更新 CLI", error.Message);
        Assert.DoesNotContain("private-token", error.Message);
    }

    [Fact]
    public async Task RepeatedCursorAndUnboundedOutputCannotKeepReaderAlive()
    {
        var responses = Handshake + """
            {"id":2,"result":{"data":[{"model":"future-model"}],"nextCursor":"loop"}}
            {"id":3,"result":{"data":[],"nextCursor":"loop"}}
            """;
        await Assert.ThrowsAsync<InvalidDataException>(() => CodexModelCatalog.ReadCatalogAsync(new StringReader(responses), new StringWriter()));
        await Assert.ThrowsAsync<InvalidDataException>(() => CodexModelCatalog.ReadCatalogAsync(new StringReader(new string('x', 300_000)), new StringWriter()));
    }

    [Fact]
    public async Task CancellationStopsProtocolRead()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CodexModelCatalog.ReadCatalogAsync(new StringReader(Handshake), new StringWriter(), cancel.Token));
    }
}
