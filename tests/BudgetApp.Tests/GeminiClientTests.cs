using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetApp.Services;

namespace BudgetApp.Tests;

/// <summary>The shared Gemini call against canned HTTP answers; the real API is never called. The receipt reader's
/// tests cover the same call through the reader (errors, timeout, log, key).</summary>
public class GeminiClientTests
{
    private const string Key = "test-key-123";
    private static readonly JsonObject Schema = new() { ["type"] = "object" };

    private static string Envelope(string text) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { parts = new object[] { new { text = "düşünce", thought = true }, new { text } } }, finishReason = "STOP" } }
    });

    private static GeminiClient Client(FakeHttpHandler handler, ListLogger<GeminiClientTests>? logger = null) =>
        new(new HttpClient(handler), new GeminiOptions { ApiKey = Key, TimeoutSeconds = 5 }, logger ?? new ListLogger<GeminiClientTests>());

    [Fact]
    public async Task GenerateAsync_ATextOnlyRequestHasJustThePrompt()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope("""{"ok":true}"""));

        await Client(handler).GenerateAsync(new GeminiRequest("Talimat", null, "simitçiye 20", Schema, "text 12 chars"), default);

        var parts = JsonNode.Parse(handler.RequestBody!)!["contents"]![0]!["parts"]!.AsArray();
        Assert.Equal("simitçiye 20", Assert.Single(parts)!["text"]!.GetValue<string>());
        Assert.Equal(Key, handler.Request!.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task GenerateAsync_SendsTheMediaBeforeThePromptAndReturnsTheAnswerWithoutThoughts()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope("""{"ok":true}"""));
        var audio = new GeminiMedia([0x1A, 0x45, 0xDF, 0xA3], "audio/webm");

        var answer = await Client(handler).GenerateAsync(new GeminiRequest("Talimat", audio, "Dinle.", Schema, "audio 1 KB"), default);

        Assert.Equal("""{"ok":true}""", answer);
        var body = JsonNode.Parse(handler.RequestBody!)!;
        var parts = body["contents"]![0]!["parts"]!.AsArray();
        Assert.Equal("audio/webm", parts[0]!["inlineData"]!["mimeType"]!.GetValue<string>());
        Assert.Equal("Dinle.", parts[1]!["text"]!.GetValue<string>());
        Assert.Equal(Schema.ToJsonString(), body["generationConfig"]!["responseJsonSchema"]!.ToJsonString());
    }

    [Fact]
    public async Task GenerateAsync_LogsTheInputLabelNotTheInput()
    {
        var logger = new ListLogger<GeminiClientTests>();

        await Client(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope("{}")), logger)
            .GenerateAsync(new GeminiRequest("Talimat", null, "simitçiye 20", Schema, "text 12 chars"), default);

        var line = Assert.Single(logger.Entries).Message;
        Assert.Contains("text 12 chars", line);
        Assert.DoesNotContain("simit", line);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, GeminiFailure.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, GeminiFailure.Misconfigured)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GeminiFailure.ServiceUnavailable)]
    public async Task GenerateAsync_TurnsHttpErrorsIntoFailures(HttpStatusCode status, GeminiFailure expected)
    {
        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            Client(FakeHttpHandler.Returning(status, """{"error":{"message":"x"}}"""))
                .GenerateAsync(new GeminiRequest("T", null, "P", Schema, "text"), default));

        Assert.Equal(expected, e.Failure);
    }
}
