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
        // The retry tests look at the main model alone; the fallback has its own tests below.
        var options = new GeminiOptions { ApiKey = Key, TimeoutSeconds = 5, FallbackModel = "" };
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
            new GeminiOptions { ApiKey = Key, TimeoutSeconds = 0.4, RetryDelaysSeconds = [0.3, 0.3], FallbackModel = "" }, new ListLogger<GeminiClientTests>());
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

    // ---------- the fallback model, once, when the main one stays overloaded ----------

    /// <summary>Answers per model: the main model with <paramref name="main"/>, the others with <paramref name="others"/>; remembers which models were asked.</summary>
    private static FakeHttpHandler ByModel(List<string> asked, HttpStatusCode main, HttpStatusCode others) =>
        new((request, _) =>
        {
            var model = request.RequestUri!.AbsolutePath.Split('/').Last().Split(':')[0];
            asked.Add(model);
            var status = model == "gemini-3.8-flash" ? main : others;
            var body = status == HttpStatusCode.OK ? Envelope("""{"model":"answered"}""") : status == HttpStatusCode.ServiceUnavailable ? Busy : """{"error":{"message":"x"}}""";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        });

    private static GeminiClient WithFallback(FakeHttpHandler handler, ListLogger<GeminiClientTests>? logger = null, Action<GeminiOptions>? configure = null) =>
        Client(handler, logger, o => { o.Model = "gemini-3.8-flash"; o.FallbackModel = "gemini-3.5-flash-lite"; configure?.Invoke(o); });

    [Fact]
    public async Task GenerateAsync_AsksTheFallbackModelOnceWhenTheMainOneStaysOverloaded()
    {
        var asked = new List<string>();
        var logger = new ListLogger<GeminiClientTests>();

        var answer = await WithFallback(ByModel(asked, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK), logger).GenerateAsync(Request, default);

        Assert.Equal("""{"model":"answered"}""", answer);
        Assert.Equal(["gemini-3.8-flash", "gemini-3.8-flash", "gemini-3.8-flash", "gemini-3.5-flash-lite"], asked);
        Assert.Contains(logger.Entries, e => e.Message.Contains("gemini-3.8-flash is still overloaded after 3 tries; using the fallback model gemini-3.5-flash-lite once"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("Gemini gemini-3.5-flash-lite answered HTTP 200"));
    }

    [Fact]
    public async Task GenerateAsync_TheFallbackIsAskedOnlyOnceAndItsBusyAnswerIsReported()
    {
        var asked = new List<string>();

        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            WithFallback(ByModel(asked, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)).GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.Overloaded, e.Failure);
        Assert.Equal(1, asked.Count(m => m == "gemini-3.5-flash-lite"));
    }

    [Fact]
    public async Task GenerateAsync_AnErrorOfTheFallbackIsReportedAsItIs()
    {
        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            WithFallback(ByModel([], HttpStatusCode.ServiceUnavailable, HttpStatusCode.NotFound)).GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.Misconfigured, e.Failure);
    }

    [Fact]
    public async Task GenerateAsync_TheMainModelAnsweringMeansNoFallback()
    {
        var asked = new List<string>();

        await WithFallback(ByModel(asked, HttpStatusCode.OK, HttpStatusCode.OK)).GenerateAsync(Request, default);

        Assert.Equal(["gemini-3.8-flash"], asked);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GenerateAsync_OnlyAnOverloadOrAQuotaSwitchesToTheFallback(HttpStatusCode status)
    {
        var asked = new List<string>();

        await Assert.ThrowsAsync<GeminiCallException>(() => WithFallback(ByModel(asked, status, HttpStatusCode.OK)).GenerateAsync(Request, default));

        Assert.Equal(["gemini-3.8-flash"], asked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("gemini-3.8-flash")]   // the same model is no fallback
    public async Task GenerateAsync_WithoutAFallbackModelTheOverloadIsReported(string? fallback)
    {
        var asked = new List<string>();

        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            WithFallback(ByModel(asked, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK), configure: o => o.FallbackModel = fallback).GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.Overloaded, e.Failure);
        Assert.All(asked, m => Assert.Equal("gemini-3.8-flash", m));
    }

    [Fact]
    public async Task GenerateAsync_UsesTheConfiguredFallbackModel()
    {
        var asked = new List<string>();

        await WithFallback(ByModel(asked, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK), configure: o => o.FallbackModel = "gemini-3.6-flash")
            .GenerateAsync(Request, default);

        Assert.Equal("gemini-3.6-flash", asked.Last());
    }

    [Fact]
    public async Task GenerateAsync_FallsBackStraightAwayWhenTheNextWaitWouldPassTheTimeout()
    {
        var asked = new List<string>();
        var waits = new List<TimeSpan>();

        var answer = await Client(ByModel(asked, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK), configure: o =>
        {
            o.Model = "gemini-3.8-flash"; o.FallbackModel = "gemini-3.5-flash-lite"; o.TimeoutSeconds = 1.5;
        }, waits: waits).GenerateAsync(Request, default);

        Assert.Equal("""{"model":"answered"}""", answer);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);          // the 2 s wait would pass 1,5 s: no third try, the fallback instead
        Assert.Equal(["gemini-3.8-flash", "gemini-3.8-flash", "gemini-3.5-flash-lite"], asked);
    }

    [Fact]
    public async Task GenerateAsync_TheFallbackStaysWithinTheSameTimeout()
    {
        // Real waits; the fallback hangs: the whole call still ends at the 0,5 s limit, as a timeout.
        var handler = new FakeHttpHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("flash-lite"))
                await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Busy) };
        });
        var client = new GeminiClient(new HttpClient(handler), new GeminiOptions
        {
            ApiKey = Key, Model = "gemini-3.8-flash", FallbackModel = "gemini-3.5-flash-lite", TimeoutSeconds = 0.5, RetryDelaysSeconds = [0.1]
        }, new ListLogger<GeminiClientTests>());
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var e = await Assert.ThrowsAsync<GeminiCallException>(() => client.GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.Timeout, e.Failure);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
    }

    [Fact]
    public void Options_FallBackToGemini35FlashLiteByDefault() =>
        Assert.Equal("gemini-3.5-flash-lite", new GeminiOptions().FallbackModel);

    // ---------- out of quota (429): the fallback model has a quota of its own ----------

    private const string DailyQuota = """{"error":{"code":429,"message":"Quota exceeded for metric: generate_content_free_tier_requests, limit: 250, model: gemini-3.8-flash. GenerateRequestsPerDayPerProjectPerModel-FreeTier","status":"RESOURCE_EXHAUSTED"}}""";
    private const string PerMinute = """{"error":{"code":429,"message":"Quota exceeded: GenerateRequestsPerMinutePerProjectPerModel-FreeTier","status":"RESOURCE_EXHAUSTED"}}""";

    /// <summary>The main model answers with <paramref name="mainBody"/> (429), the fallback with 200 or its own 429 body.</summary>
    private static FakeHttpHandler Quota(List<string> asked, string mainBody, string? fallbackBody = null) =>
        new((request, _) =>
        {
            var model = request.RequestUri!.AbsolutePath.Split('/').Last().Split(':')[0];
            asked.Add(model);
            var (status, body) = model == "gemini-3.8-flash" ? (HttpStatusCode.TooManyRequests, mainBody)
                : fallbackBody is null ? (HttpStatusCode.OK, Envelope("""{"model":"answered"}"""))
                : (HttpStatusCode.TooManyRequests, fallbackBody);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        });

    [Theory]
    [InlineData(true)]   // the day's quota of the main model is used up
    [InlineData(false)]  // too many requests to the main model this minute
    public async Task GenerateAsync_OutOfQuotaAsksTheFallbackModelOnceWithoutRetrying(bool daily)
    {
        var asked = new List<string>();
        var waits = new List<TimeSpan>();
        var logger = new ListLogger<GeminiClientTests>();

        var answer = await Client(Quota(asked, daily ? DailyQuota : PerMinute), logger,
            o => { o.Model = "gemini-3.8-flash"; o.FallbackModel = "gemini-3.5-flash-lite"; }, waits).GenerateAsync(Request, default);

        Assert.Equal("""{"model":"answered"}""", answer);
        Assert.Equal(["gemini-3.8-flash", "gemini-3.5-flash-lite"], asked);   // one try each, no retry of a 429
        Assert.Empty(waits);
        Assert.Contains(logger.Entries, e => e.Message.Contains(
            $"gemini-3.8-flash is out of quota ({(daily ? "daily quota" : "rate limit")}); using the fallback model gemini-3.5-flash-lite once"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("Gemini gemini-3.5-flash-lite answered HTTP 200"));
    }

    [Fact]
    public async Task GenerateAsync_WhenTheFallbackIsOutOfQuotaTooTheQuotaIsReported()
    {
        var asked = new List<string>();

        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            WithFallback(Quota(asked, DailyQuota, DailyQuota)).GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.QuotaExceeded, e.Failure);
        Assert.Equal(["gemini-3.8-flash", "gemini-3.5-flash-lite"], asked);
    }

    [Theory]
    [InlineData(true, GeminiFailure.QuotaExceeded)]
    [InlineData(false, GeminiFailure.RateLimited)]
    public async Task GenerateAsync_TheFallbacksOwn429DecidesTheMessage(bool fallbackDaily, GeminiFailure expected)
    {
        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            WithFallback(Quota([], DailyQuota, fallbackDaily ? DailyQuota : PerMinute)).GenerateAsync(Request, default));

        Assert.Equal(expected, e.Failure);
    }

    [Fact]
    public async Task GenerateAsync_OutOfQuotaWithoutAFallbackIsReportedStraightAway()
    {
        var asked = new List<string>();

        var e = await Assert.ThrowsAsync<GeminiCallException>(() =>
            WithFallback(Quota(asked, DailyQuota), configure: o => o.FallbackModel = "").GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.QuotaExceeded, e.Failure);
        Assert.Equal(["gemini-3.8-flash"], asked);
    }

    [Fact]
    public async Task GenerateAsync_AnOverloadedFallbackAfterAQuotaIsReportedAsBusy()
    {
        var handler = new FakeHttpHandler((request, _) =>
        {
            var main = request.RequestUri!.AbsolutePath.Contains("gemini-3.8-flash");
            return Task.FromResult(new HttpResponseMessage(main ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(main ? DailyQuota : Busy)
            });
        });

        var e = await Assert.ThrowsAsync<GeminiCallException>(() => WithFallback(handler).GenerateAsync(Request, default));

        Assert.Equal(GeminiFailure.Overloaded, e.Failure);
        Assert.Equal(2, handler.Calls);   // the fallback is asked once, not retried
    }
}
