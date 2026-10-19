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

    private static GeminiClient Client(FakeHttpHandler handler, ListLogger<GeminiClientTests>? logger = null,
        Action<GeminiOptions>? configure = null, List<TimeSpan>? waits = null)
    {
        var options = new GeminiOptions { ApiKey = Key, TimeoutSeconds = 5 };
        configure?.Invoke(options);
        // Records the waits instead of waiting.
        return new(new HttpClient(handler), options, logger ?? new ListLogger<GeminiClientTests>(),
            (wait, _) => { waits?.Add(wait); return Task.CompletedTask; });
    }

    private static readonly GeminiRequest Request = new("T", null, "P", Schema, "text");

    private const string Busy = """{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}""";

    /// <summary>Answers with the given statuses one after the other (the last one repeats), a success as Envelope("{}").</summary>
    private static FakeHttpHandler Sequence(params HttpStatusCode[] statuses)
    {
        var call = 0;
        return new((_, _) =>
        {
            var status = statuses[Math.Min(call++, statuses.Length - 1)];
            var body = status == HttpStatusCode.OK ? Envelope("""{"ok":true}""") : status == HttpStatusCode.ServiceUnavailable ? Busy : """{"error":{"message":"x"}}""";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        });
    }

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
    [InlineData(HttpStatusCode.InternalServerError, GeminiFailure.ServiceUnavailable)]
    public async Task GenerateAsync_TurnsHttpErrorsIntoFailures(HttpStatusCode status, GeminiFailure expected)
    {
        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            Client(FakeHttpHandler.Returning(status, """{"error":{"message":"x"}}"""))
                .GenerateAsync(new GeminiRequest("T", null, "P", Schema, "text"), default));

        Assert.Equal(expected, e.Failure);
    }

    // ---------- an overloaded model (503) is asked again ----------

    [Fact]
    public async Task GenerateAsync_AsksAgainWhenTheModelIsBusyAndReturnsTheLaterAnswer()
    {
        var handler = Sequence(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var waits = new List<TimeSpan>();

        var answer = await Client(handler, waits: waits).GenerateAsync(Request, default);

        Assert.Equal("""{"ok":true}""", answer);
        Assert.Equal(2, handler.Calls);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);
    }

    [Fact]
    public async Task GenerateAsync_WaitsLongerEachTimeAndGivesUpAsOverloaded()
    {
        var handler = Sequence(HttpStatusCode.ServiceUnavailable);
        var waits = new List<TimeSpan>();

        var e = await Assert.ThrowsAsync<GeminiCallException>(() => Client(handler, waits: waits).GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.Overloaded, e.Failure);
        Assert.Equal(3, handler.Calls);                                         // the first try and two retries
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task GenerateAsync_TakesTheWaitsFromTheSettings()
    {
        var handler = Sequence(HttpStatusCode.ServiceUnavailable);
        var waits = new List<TimeSpan>();

        await Assert.ThrowsAsync<GeminiCallException>(() =>
            Client(handler, configure: o => o.RetryDelaysSeconds = [0.5, 1, 3], waits: waits).GenerateAsync(Request, default));

        Assert.Equal(4, handler.Calls);
        Assert.Equal([TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)], waits);
    }

    [Fact]
    public async Task GenerateAsync_DoesNotStartAWaitThatWouldPassTheTimeout()
    {
        var handler = Sequence(HttpStatusCode.ServiceUnavailable);
        var waits = new List<TimeSpan>();
        var logger = new ListLogger<GeminiClientTests>();

        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            Client(handler, logger, o => o.TimeoutSeconds = 1.5, waits).GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.Overloaded, e.Failure);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);                         // 1 s fits in 1,5 s, the next 2 s does not
        Assert.Equal(2, handler.Calls);
        Assert.Contains(logger.Entries, l => l.Message.Contains("not trying again"));
    }

    [Fact]
    public async Task GenerateAsync_TheWholeCallStaysWithinTheTimeout()
    {
        // Real waits: 2 x 0,3 s would end after the 0,4 s limit, so the second wait is never started.
        var handler = Sequence(HttpStatusCode.ServiceUnavailable);
        var client = new GeminiClient(new HttpClient(handler),
            new GeminiOptions { ApiKey = Key, TimeoutSeconds = 0.4, RetryDelaysSeconds = [0.3, 0.3] }, new ListLogger<GeminiClientTests>());
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAsync<GeminiCallException>(() => client.GenerateAsync(Request, default));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), watch.Elapsed.ToString());
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]      // the quota: trying again would only use more of it
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GenerateAsync_OtherErrorsAreNotRetried(HttpStatusCode status)
    {
        var handler = Sequence(status);
        var waits = new List<TimeSpan>();

        await Assert.ThrowsAsync<GeminiCallException>(() => Client(handler, waits: waits).GenerateAsync(Request, default));

        Assert.Equal(1, handler.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task GenerateAsync_WithoutWaitsInTheSettingsDoesNotRetry()
    {
        var handler = Sequence(HttpStatusCode.ServiceUnavailable);

        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            Client(handler, configure: o => o.RetryDelaysSeconds = []).GenerateAsync(Request, default));

        Assert.Equal((GeminiFailure.Overloaded, 1), (e.Failure, handler.Calls));
    }

    [Fact]
    public async Task GenerateAsync_LogsEveryTryAndTheRetries()
    {
        var logger = new ListLogger<GeminiClientTests>();

        await Client(Sequence(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK), logger)
            .GenerateAsync(Request, default);

        Assert.Equal(2, logger.Entries.Count(e => e.Message.Contains("answered HTTP 503")));
        Assert.Contains(logger.Entries, e => e.Message.Contains("trying again in 1 s (retry 1 of 2)"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("trying again in 2 s (retry 2 of 2)"));
        Assert.Single(logger.Entries, e => e.Message.Contains("answered HTTP 200"));
        Assert.All(logger.Entries, e => Assert.DoesNotContain(Key, e.Message));
    }

    [Fact]
    public void Options_RetryAfterOneAndThenTwoSecondsByDefault() =>
        Assert.Equal([1d, 2d], new GeminiOptions().RetryDelaysSeconds);
}
