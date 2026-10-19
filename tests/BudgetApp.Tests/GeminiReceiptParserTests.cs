using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetApp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BudgetApp.Tests;

/// <summary>Gemini receipt reading against canned HTTP answers; the real API is never called.</summary>
public class GeminiReceiptParserTests
{
    private const string Key = "test-key-123";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
    private static readonly ReceiptImage Image = new(Jpeg, "image/jpeg");
    private static readonly string[] Categories = ["Market", "Kafe/Kahve", "Diğer Gider"];

    private static GeminiReceiptParser Parser(FakeHttpHandler handler, Action<GeminiOptions>? configure = null,
        ILogger<GeminiReceiptParser>? logger = null)
    {
        // Retries keep their count but do not really wait, so a 503 test does not take seconds.
        var options = new GeminiOptions { ApiKey = Key, Model = "gemini-3.8-flash", TimeoutSeconds = 5, RetryDelaysSeconds = [0, 0] };
        configure?.Invoke(options);
        return new GeminiReceiptParser(new HttpClient(handler), Options.Create(options), logger ?? NullLogger<GeminiReceiptParser>.Instance);
    }

    /// <summary>A successful generateContent answer whose text is <paramref name="answer"/>.</summary>
    private static string Envelope(object answer, string finishReason = "STOP") =>
        JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new { role = "model", parts = new[] { new { text = answer as string ?? JsonSerializer.Serialize(answer) } } },
                    finishReason
                }
            }
        });

    private static readonly object GroceryAnswer = new
    {
        isReceipt = true,
        storeName = "Köşe Market",
        date = "2026-10-15",
        total = 76.5,
        generalDiscount = (decimal?)null,
        lines = new object[]
        {
            new { rawText = "SUT YRM YGL 1L", name = "Yarım yağlı süt 1 L", quantity = 1, unit = "adet", amount = 42.5, category = "Market" },
            new { rawText = "MAKARNA 2 X 17,00", name = "Makarna", quantity = 2, unit = "adet", amount = 34.0, category = "Market" }
        }
    };

    private static Task<ReceiptParseResult> Parse(FakeHttpHandler handler, Action<GeminiOptions>? configure = null) =>
        Parser(handler, configure).ParseAsync(Image, Categories, CancellationToken.None);

    private static async Task<ReceiptErrorKind> FailureOf(FakeHttpHandler handler, Action<GeminiOptions>? configure = null) =>
        (await Assert.ThrowsAsync<ReceiptParseException>(() => Parse(handler, configure))).Kind;

    // ---------- the request ----------

    [Fact]
    public async Task ParseAsync_PostsToTheConfiguredModelWithTheKeyInAHeaderNotInTheUrl()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));

        await Parse(handler, o => o.Model = "gemini-3.5-flash-lite");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent",
            handler.Request.RequestUri!.ToString());
        Assert.Equal(Key, handler.Request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(Key, handler.Request.RequestUri.ToString());
        Assert.DoesNotContain(Key, handler.RequestBody);
    }

    [Fact]
    public async Task ParseAsync_SendsThePictureInlineWithTheInstructionsAndAJsonSchema()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));

        await Parse(handler);

        var body = JsonNode.Parse(handler.RequestBody!)!;
        var parts = body["contents"]![0]!["parts"]!.AsArray();
        Assert.Equal("image/jpeg", parts[0]!["inlineData"]!["mimeType"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(Jpeg), parts[0]!["inlineData"]!["data"]!.GetValue<string>());
        Assert.Contains("Türk biçimindedir", body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());

        var config = body["generationConfig"]!;
        Assert.Equal("application/json", config["responseMimeType"]!.GetValue<string>());
        var schema = config["responseJsonSchema"]!;
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Contains("lines", schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task ParseAsync_SendsNoSamplingParameters()
    {
        // Gemini 3.6 and later ignore temperature/topP/topK and future models reject them.
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));

        await Parse(handler);

        var config = JsonNode.Parse(handler.RequestBody!)!["generationConfig"]!.AsObject();
        Assert.False(config.ContainsKey("temperature"));
        Assert.False(config.ContainsKey("topP"));
        Assert.False(config.ContainsKey("topK"));
    }

    [Fact]
    public void Options_DefaultToTheLowestThinkingLevelOfTheModelAndASixtySecondTimeout()
    {
        var options = new GeminiOptions();

        Assert.Equal(("gemini-3.8-flash", "low", 60d), (options.Model, options.ThinkingLevel, options.TimeoutSeconds));
    }

    [Fact]
    public async Task ParseAsync_LimitsTheCategoryToTheGivenExpenseCategories()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));

        await Parse(handler);

        var category = JsonNode.Parse(handler.RequestBody!)!["generationConfig"]!["responseJsonSchema"]!
            ["properties"]!["lines"]!["items"]!["properties"]!["category"]!;
        Assert.Equal(Categories, category["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Theory]
    [InlineData("low")]   // the default: the lowest level gemini-3.8-flash accepts
    [InlineData("medium")]
    [InlineData("minimal")] // other models accept it; the setting is passed on as written
    public async Task ParseAsync_SendsTheConfiguredThinkingLevel(string level)
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));

        await Parse(handler, o => o.ThinkingLevel = level);

        Assert.Equal(level, JsonNode.Parse(handler.RequestBody!)!["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ParseAsync_AnEmptyThinkingLevelLeavesItToTheModel(string? level)
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));

        await Parse(handler, o => o.ThinkingLevel = level);

        Assert.Null(JsonNode.Parse(handler.RequestBody!)!["generationConfig"]!["thinkingConfig"]);
    }

    [Fact]
    public async Task ParseAsync_UsesTheDefaultThinkingLevelWhenNothingIsConfigured()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));
        var parser = new GeminiReceiptParser(new HttpClient(handler), Options.Create(new GeminiOptions { ApiKey = Key }),
            NullLogger<GeminiReceiptParser>.Instance);

        await parser.ParseAsync(Image, Categories, default);

        Assert.Equal("low", JsonNode.Parse(handler.RequestBody!)!["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>());
    }

    // ---------- the log ----------

    [Fact]
    public async Task ParseAsync_LogsHowLongTheAnswerTookItsStatusAndTheTokens()
    {
        var logger = new ListLogger<GeminiReceiptParser>();
        var body = JsonNode.Parse(Envelope(GroceryAnswer))!.AsObject();
        body["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 1290, ["candidatesTokenCount"] = 210, ["thoughtsTokenCount"] = 340 };

        await Parser(FakeHttpHandler.Returning(HttpStatusCode.OK, body.ToJsonString()), logger: logger).ParseAsync(Image, Categories, default);

        var line = Assert.Single(logger.Entries, e => e.Message.Contains("answered HTTP 200")).Message;
        Assert.Matches(@"in \d+ ms", line);
        Assert.Contains("gemini-3.8-flash", line);
        Assert.Contains("thinking low", line);
        Assert.Contains("finish STOP", line);
        Assert.Contains("prompt 1290, answer 210, thinking 340", line);
    }

    [Fact]
    public async Task ParseAsync_LogsAnErrorStatusWithTheTime()
    {
        var logger = new ListLogger<GeminiReceiptParser>();

        await Assert.ThrowsAsync<ReceiptParseException>(() =>
            Parser(FakeHttpHandler.Returning(HttpStatusCode.InternalServerError, """{"error":{"code":500,"message":"internal"}}"""), logger: logger)
                .ParseAsync(Image, Categories, default));

        var line = Assert.Single(logger.Entries, e => e.Message.Contains("HTTP 500")).Message;
        Assert.Matches(@"in \d+ ms", line);
        Assert.Contains("internal", line);
        Assert.Equal(LogLevel.Warning, logger.Entries.Single(e => e.Message.Contains("HTTP 500")).Level);
    }

    [Fact]
    public async Task ParseAsync_LogsATimeoutWithTheWaitedTime()
    {
        var logger = new ListLogger<GeminiReceiptParser>();

        await Assert.ThrowsAsync<ReceiptParseException>(() =>
            Parser(FakeHttpHandler.Hanging(), o => o.TimeoutSeconds = 0.2, logger).ParseAsync(Image, Categories, default));

        var line = Assert.Single(logger.Entries).Message;
        Assert.Contains("gave no answer within 0,2 s", line.Replace("0.2", "0,2"));
        Assert.Matches(@"\(\d+ ms", line);
    }

    [Fact]
    public async Task ParseAsync_NeverWritesTheKeyToTheLog()
    {
        var logger = new ListLogger<GeminiReceiptParser>();
        // Google's error text quoting the request must not make the key appear either: it is not in the URL or body.
        foreach (var handler in new[]
                 {
                     FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer)),
                     FakeHttpHandler.Returning(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"API key not valid."}}"""),
                     FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope("kırık")),
                     FakeHttpHandler.Throwing(new HttpRequestException("DNS")),
                     FakeHttpHandler.Hanging()
                 })
        {
            try { await Parser(handler, o => o.TimeoutSeconds = 0.2, logger).ParseAsync(Image, Categories, default); }
            catch (ReceiptParseException) { }
        }

        Assert.True(logger.Entries.Count >= 5);
        Assert.All(logger.Entries, e => Assert.DoesNotContain(Key, e.Message));
    }

    [Fact]
    public async Task ParseAsync_WithoutAKeyDoesNotCallAnything()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer));

        Assert.Equal(ReceiptErrorKind.Misconfigured, await FailureOf(handler, o => o.ApiKey = ""));
        Assert.Equal(0, handler.Calls);
    }

    // ---------- the answer ----------

    [Fact]
    public async Task ParseAsync_ReadsStoreDateTotalAndLines()
    {
        var result = await Parse(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer)));

        Assert.True(result.IsReceipt);
        Assert.Equal("Köşe Market", result.StoreName);
        Assert.Equal(new DateTime(2026, 10, 15), result.Date);
        Assert.Equal(76.5m, result.Total);
        Assert.Equal(0m, result.GeneralDiscount);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(new ReceiptLine("SUT YRM YGL 1L", "Yarım yağlı süt 1 L", 1m, "adet", 42.5m, "Market"), result.Lines[0]);
        Assert.Equal((2m, 34.0m), (result.Lines[1].Quantity, result.Lines[1].Amount));
    }

    [Fact]
    public async Task ParseAsync_KeepsKurusExactlyAsDecimals()
    {
        var answer = """{"isReceipt":true,"storeName":null,"date":null,"total":1234.56,"generalDiscount":null,"lines":[{"rawText":"A","name":"A","quantity":null,"unit":null,"amount":0.1,"category":"Market"},{"rawText":"B","name":"B","quantity":null,"unit":null,"amount":1234.46,"category":"Market"}]}""";

        var result = await Parse(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(answer)));

        Assert.Equal(1234.56m, result.Lines.Sum(l => l.Amount));
        Assert.Equal(1234.56m, result.Total);
        Assert.Null(result.Date);
        Assert.Null(result.StoreName);
    }

    [Fact]
    public async Task ParseAsync_ReadsAGeneralDiscountAndTurnsANegativeLineIntoOne()
    {
        var answer = new
        {
            isReceipt = true, storeName = "X", date = "2026-10-15", total = 90, generalDiscount = 5,
            lines = new object[]
            {
                new { rawText = "CAY", name = "Çay", quantity = (decimal?)null, unit = (string?)null, amount = 100, category = "Market" },
                new { rawText = "KAMPANYA INDIRIMI", name = "Kampanya indirimi", quantity = (decimal?)null, unit = (string?)null, amount = -5, category = "Market" }
            }
        };

        var result = await Parse(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(answer)));

        Assert.Single(result.Lines);
        Assert.Equal(10m, result.GeneralDiscount);
    }

    [Fact]
    public async Task ParseAsync_FallsBackToTheRawTextWhenTheNameIsEmptyAndIgnoresAnUnreadableDate()
    {
        var answer = """{"isReceipt":true,"storeName":"  ","date":"15.10.2026","total":null,"generalDiscount":null,"lines":[{"rawText":"KRK 5LI","name":"","quantity":null,"unit":" ","amount":30,"category":"Market"}]}""";

        var result = await Parse(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(answer)));

        Assert.Equal("KRK 5LI", result.Lines[0].Name);
        Assert.Null(result.Lines[0].Unit);
        Assert.Null(result.Date);
        Assert.Null(result.StoreName);
    }

    [Fact]
    public async Task ParseAsync_ReportsAPictureThatIsNotAReceipt()
    {
        var answer = new { isReceipt = false, storeName = (string?)null, date = (string?)null, total = (decimal?)null, generalDiscount = (decimal?)null, lines = Array.Empty<object>() };

        var result = await Parse(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(answer)));

        Assert.False(result.IsReceipt);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task ParseAsync_SkipsThoughtParts()
    {
        var body = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new { parts = new object[] { new { text = "düşünüyorum...", thought = true }, new { text = JsonSerializer.Serialize(GroceryAnswer) } } },
                    finishReason = "STOP"
                }
            }
        });

        var result = await Parse(FakeHttpHandler.Returning(HttpStatusCode.OK, body));

        Assert.Equal(2, result.Lines.Count);
    }

    [Theory]
    [InlineData("bu bir JSON değil")]
    [InlineData("""{"isReceipt":true,"lines":[{"rawText":"A","name":"A","amount":"on lira"}]}""")]
    [InlineData("""{"isReceipt":true,"lines":[{"rawText":"A","name":"A","amount":null,"category":"Market"}]}""")]
    [InlineData("""{"isReceipt":true,"lines":[{"rawText":"","name":"","amount":5,"category":"Market"}]}""")]
    [InlineData("null")]
    [InlineData("   ")]
    public async Task ParseAsync_AnAnswerThatIsNotTheAgreedJsonIsAnInvalidResponse(string answer) =>
        Assert.Equal(ReceiptErrorKind.InvalidResponse, await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(answer))));

    [Fact]
    public async Task ParseAsync_AnEnvelopeThatIsNotJsonIsAnInvalidResponse() =>
        Assert.Equal(ReceiptErrorKind.InvalidResponse, await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.OK, "<html>")));

    [Fact]
    public async Task ParseAsync_AnAnswerCutOffByTheTokenLimitIsAnInvalidResponse() =>
        Assert.Equal(ReceiptErrorKind.InvalidResponse,
            await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope("""{"isReceipt":true,"lines":[{"rawT""", "MAX_TOKENS"))));

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("PROHIBITED_CONTENT")]
    [InlineData("IMAGE_SAFETY")]
    public async Task ParseAsync_ABlockedAnswerSaysSo(string finishReason) =>
        Assert.Equal(ReceiptErrorKind.Blocked, await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.OK, Envelope(GroceryAnswer, finishReason))));

    [Fact]
    public async Task ParseAsync_ABlockedPromptWithoutCandidatesSaysSo() =>
        Assert.Equal(ReceiptErrorKind.Blocked,
            await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"IMAGE_SAFETY"}}""")));

    [Fact]
    public async Task ParseAsync_NoCandidatesAtAllIsAnInvalidResponse() =>
        Assert.Equal(ReceiptErrorKind.InvalidResponse, await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.OK, "{}")));

    // ---------- errors ----------

    [Fact]
    public async Task ParseAsync_TellsTheDailyQuotaApartFromTheRateLimit()
    {
        const string daily = """{"error":{"code":429,"message":"Quota exceeded for metric: generate_content_free_tier_requests, limit: 250, model: gemini-3.8-flash. GenerateRequestsPerDayPerProjectPerModel-FreeTier","status":"RESOURCE_EXHAUSTED"}}""";
        const string perMinute = """{"error":{"code":429,"message":"Quota exceeded: GenerateRequestsPerMinutePerProjectPerModel-FreeTier","status":"RESOURCE_EXHAUSTED"}}""";

        Assert.Equal(ReceiptErrorKind.QuotaExceeded, await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.TooManyRequests, daily)));
        Assert.Equal(ReceiptErrorKind.RateLimited, await FailureOf(FakeHttpHandler.Returning(HttpStatusCode.TooManyRequests, perMinute)));
    }

    [Theory]
    [InlineData(400, ReceiptErrorKind.Misconfigured)]   // e.g. a field the model does not know
    [InlineData(403, ReceiptErrorKind.Misconfigured)]   // wrong key
    [InlineData(404, ReceiptErrorKind.Misconfigured)]   // unknown model
    [InlineData(500, ReceiptErrorKind.ServiceUnavailable)]
    [InlineData(502, ReceiptErrorKind.ServiceUnavailable)]
    [InlineData(503, ReceiptErrorKind.Overloaded)]       // still busy after the retries
    [InlineData(504, ReceiptErrorKind.Timeout)]
    public async Task ParseAsync_MapsHttpErrors(int status, ReceiptErrorKind expected) =>
        Assert.Equal(expected, await FailureOf(FakeHttpHandler.Returning((HttpStatusCode)status, """{"error":{"code":1,"message":"x"}}""")));

    [Fact]
    public async Task ParseAsync_GivesUpAfterTheTimeout()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Equal(ReceiptErrorKind.Timeout, await FailureOf(FakeHttpHandler.Hanging(), o => o.TimeoutSeconds = 0.2));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ParseAsync_ANetworkFailureMeansTheServiceIsUnavailable() =>
        Assert.Equal(ReceiptErrorKind.ServiceUnavailable, await FailureOf(FakeHttpHandler.Throwing(new HttpRequestException("DNS"))));

    [Fact]
    public async Task ParseAsync_AUserWhoLeavesCancelsTheRequestWithoutCallingItATimeout()
    {
        using var cancel = new CancellationTokenSource();
        var parser = Parser(FakeHttpHandler.Hanging());

        var task = parser.ParseAsync(Image, Categories, cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void ErrorMessagesAreTurkishSentencesForTheUser()
    {
        Assert.Equal("Fiş okunması uzun sürdü. Biraz sonra tekrar dene.", new ReceiptParseException(ReceiptErrorKind.Timeout).Message);
        Assert.Equal("Bugünkü fiş hakkını kullandın (10/10). Yarın tekrar dene ya da işlemleri elle ekle.",
            new ReceiptParseException(ReceiptErrorKind.UserDailyLimit, limit: 10).Message);
        Assert.All(Enum.GetValues<ReceiptErrorKind>(), kind => Assert.False(string.IsNullOrWhiteSpace(ReceiptMessages.For(kind))));
        // nothing technical reaches the user
        Assert.Equal(ReceiptMessages.For(ReceiptErrorKind.Misconfigured), new ReceiptParseException(ReceiptErrorKind.Misconfigured, "403 API key not valid").Message);
    }

    [Fact]
    public void ProviderIsGeminiAndNotADemo()
    {
        var parser = Parser(FakeHttpHandler.Returning(HttpStatusCode.OK, "{}"));
        Assert.Equal(("gemini", false), (parser.Provider, parser.IsDemo));
    }

    [Fact]
    public async Task ParseAsync_ReadsTheFallbackModelsAnswerWhenTheMainModelStaysOverloaded()
    {
        var asked = new List<string>();
        var handler = new FakeHttpHandler((request, _) =>
        {
            asked.Add(request.RequestUri!.AbsolutePath);
            var lite = request.RequestUri.AbsolutePath.Contains("gemini-3.5-flash-lite");
            return Task.FromResult(new HttpResponseMessage(lite ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
            {
                Content = new System.Net.Http.StringContent(lite ? Envelope(GroceryAnswer) : """{"error":{"code":503,"status":"UNAVAILABLE"}}""")
            });
        });

        var result = await Parser(handler).ParseAsync(Image, Categories, default);

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(4, asked.Count);
        Assert.EndsWith("/models/gemini-3.5-flash-lite:generateContent", asked[^1]);
    }
}
