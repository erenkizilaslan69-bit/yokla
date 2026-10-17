using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BudgetApp.Services;

/// <summary>Why a Gemini call gave no usable answer. Each feature turns it into its own message for the user.</summary>
public enum GeminiFailure
{
    /// <summary>A problem on our side: no or wrong key, unknown model, bad request. Details go to the log only.</summary>
    Misconfigured,
    Timeout,
    /// <summary>The provider is down, overloaded, failed or could not be reached.</summary>
    ServiceUnavailable,
    /// <summary>Too many requests in a short time at the provider.</summary>
    RateLimited,
    /// <summary>The provider's daily quota is used up.</summary>
    QuotaExceeded,
    /// <summary>The provider refused the input or the answer (safety filters).</summary>
    Blocked,
    /// <summary>The answer could not be understood (not JSON, empty, cut off).</summary>
    InvalidResponse
}

public sealed class GeminiCallException(GeminiFailure failure, string detail) : Exception(detail)
{
    public GeminiFailure Failure { get; } = failure;
}

/// <summary>One file (a picture, a voice recording) sent inline with the request; held in memory only.</summary>
public sealed record GeminiMedia(byte[] Data, string MimeType);

/// <param name="Instructions">The system instruction.</param>
/// <param name="Media">Sent before the prompt; null for a text-only request.</param>
/// <param name="Prompt">The user turn's text.</param>
/// <param name="Schema">The JSON schema the answer must follow.</param>
/// <param name="InputLabel">What was sent, for the log ("image 120 KB"); never the content itself.</param>
public sealed record GeminiRequest(string Instructions, GeminiMedia? Media, string Prompt, JsonObject Schema, string InputLabel);

/// <summary>
/// One "generateContent" call to the Google Gemini API (which keeps nothing on Google's side between calls), over plain
/// HttpClient, with the answer constrained to a JSON schema. The API key travels in the "x-goog-api-key" header, never
/// in the URL, so it cannot end up in a log line. Used by the receipt reader and quick entry; the log lines go to the
/// caller's logger.
/// </summary>
public sealed class GeminiClient(HttpClient http, GeminiOptions settings, ILogger logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sends the request and returns the answer's text (the JSON), thought summaries left out.</summary>
    /// <exception cref="GeminiCallException">No usable answer; the details are logged.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled (the user left).</exception>
    public async Task<string> GenerateAsync(GeminiRequest call, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new GeminiCallException(GeminiFailure.Misconfigured, "Gemini:ApiKey is not set.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{settings.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(settings.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        request.Content = new StringContent(BuildRequestBody(call, settings).ToJsonString(), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        // What every log line of this request says. Never the key: it only lives in the request header above.
        var thinking = string.IsNullOrWhiteSpace(settings.ThinkingLevel) ? "model default" : settings.ThinkingLevel;
        var input = call.InputLabel;
        var watch = Stopwatch.StartNew();

        string body;
        HttpStatusCode status;
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Gemini {Model} gave no answer within {TimeoutSeconds} s ({ElapsedMs} ms; thinking {Thinking}; {Input}).",
                settings.Model, settings.TimeoutSeconds, watch.ElapsedMilliseconds, thinking, input);
            throw new GeminiCallException(GeminiFailure.Timeout, $"No answer within {settings.TimeoutSeconds} s.");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Gemini {Model} request cancelled by the user after {ElapsedMs} ms.", settings.Model, watch.ElapsedMilliseconds);
            throw;
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning("Gemini {Model} could not be reached after {ElapsedMs} ms: {Error}", settings.Model, watch.ElapsedMilliseconds, e.Message);
            throw new GeminiCallException(GeminiFailure.ServiceUnavailable, e.Message);
        }

        if ((int)status is < 200 or > 299)
        {
            var error = FromStatus(status, body);
            // The body of an error holds Google's explanation, never the input or the key.
            logger.LogWarning("Gemini {Model} answered HTTP {Status} in {ElapsedMs} ms ({Kind}; thinking {Thinking}; {Input}): {Body}",
                settings.Model, (int)status, watch.ElapsedMilliseconds, error.Failure, thinking, input, Truncate(body));
            throw error;
        }

        var envelope = ReadEnvelope(body);
        var usage = envelope.UsageMetadata;
        logger.LogInformation(
            "Gemini {Model} answered HTTP {Status} in {ElapsedMs} ms (thinking {Thinking}; {Input}; finish {FinishReason}; " +
            "tokens: prompt {PromptTokens}, answer {AnswerTokens}, thinking {ThoughtTokens}).",
            settings.Model, (int)status, watch.ElapsedMilliseconds, thinking, input,
            envelope.Candidates?.FirstOrDefault()?.FinishReason ?? "-",
            usage?.PromptTokenCount, usage?.CandidatesTokenCount, usage?.ThoughtsTokenCount);

        return AnswerText(envelope);
    }

    // ---------- request ----------

    internal static JsonObject BuildRequestBody(GeminiRequest call, GeminiOptions settings)
    {
        // No temperature: Gemini 3.6 and later ignore the sampling parameters and future models reject them.
        var generationConfig = new JsonObject
        {
            ["responseMimeType"] = "application/json",
            // A copy: a JSON node can have only one parent, and a caller may send the same schema again.
            ["responseJsonSchema"] = call.Schema.DeepClone()
        };
        if (!string.IsNullOrWhiteSpace(settings.ThinkingLevel))
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = settings.ThinkingLevel };

        var parts = new JsonArray();
        if (call.Media is { } media)
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = media.MimeType,
                    ["data"] = Convert.ToBase64String(media.Data)
                }
            });
        parts.Add(new JsonObject { ["text"] = call.Prompt });

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = call.Instructions })
            },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts }),
            ["generationConfig"] = generationConfig
        };
    }

    /// <summary>A schema property that may be null.</summary>
    internal static JsonObject Nullable(string type, string description) => new()
    {
        ["type"] = new JsonArray(type, "null"),
        ["description"] = description
    };

    internal static JsonArray Enum(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    // ---------- response ----------

    private static GeminiCallException FromStatus(HttpStatusCode status, string body) => (int)status switch
    {
        // Gemini answers 429 both for "too many per minute" and for "today's quota is gone"; the text tells them apart.
        429 when body.Contains("per day", StringComparison.OrdinalIgnoreCase)
                 || body.Contains("PerDay", StringComparison.Ordinal)
                 || body.Contains("quota_exceeded", StringComparison.OrdinalIgnoreCase)
            => new(GeminiFailure.QuotaExceeded, "429: daily quota"),
        429 => new(GeminiFailure.RateLimited, "429"),
        408 or 504 => new(GeminiFailure.Timeout, $"{(int)status}"),
        400 or 401 or 402 or 403 or 404 => new(GeminiFailure.Misconfigured, $"{(int)status}"),
        _ => new(GeminiFailure.ServiceUnavailable, $"{(int)status}")
    };

    private static GeminiResponse ReadEnvelope(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<GeminiResponse>(body, Json)
                   ?? throw new GeminiCallException(GeminiFailure.InvalidResponse, "Envelope is null.");
        }
        catch (JsonException e)
        {
            throw new GeminiCallException(GeminiFailure.InvalidResponse, "Envelope is not JSON: " + e.Message);
        }
    }

    private static string AnswerText(GeminiResponse response)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        if (candidate is null)
        {
            if (response.PromptFeedback?.BlockReason is { } blockReason)
                throw new GeminiCallException(GeminiFailure.Blocked, "Prompt blocked: " + blockReason);
            throw new GeminiCallException(GeminiFailure.InvalidResponse, "No candidates.");
        }

        switch (candidate.FinishReason)
        {
            case "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "IMAGE_SAFETY" or "SPII" or "RECITATION":
                throw new GeminiCallException(GeminiFailure.Blocked, "Finish reason: " + candidate.FinishReason);
            case "MAX_TOKENS":
                throw new GeminiCallException(GeminiFailure.InvalidResponse, "The answer was cut off (MAX_TOKENS).");
        }

        // Thought summaries, if any, are not part of the answer.
        var text = string.Concat((candidate.Content?.Parts ?? []).Where(p => p.Thought != true).Select(p => p.Text));
        if (string.IsNullOrWhiteSpace(text))
            throw new GeminiCallException(GeminiFailure.InvalidResponse, "Empty answer.");
        return text;
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500] + "…";

    private sealed class GeminiResponse
    {
        public List<Candidate>? Candidates { get; set; }
        public Feedback? PromptFeedback { get; set; }
        public Usage? UsageMetadata { get; set; }
    }

    /// <summary>Token counts, for the log: a long wait usually shows up as many thinking tokens.</summary>
    private sealed class Usage
    {
        public int? PromptTokenCount { get; set; }
        public int? CandidatesTokenCount { get; set; }
        public int? ThoughtsTokenCount { get; set; }
    }

    private sealed class Candidate
    {
        public Content? Content { get; set; }
        public string? FinishReason { get; set; }
    }

    private sealed class Content
    {
        public List<Part>? Parts { get; set; }
    }

    private sealed class Part
    {
        public string? Text { get; set; }
        public bool? Thought { get; set; }
    }

    private sealed class Feedback
    {
        public string? BlockReason { get; set; }
    }
}
