using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace BudgetApp.Services;

/// <summary>
/// Reads receipts with the Google Gemini API ("generateContent", which keeps nothing on Google's side between calls),
/// over plain HttpClient. The answer is constrained to a JSON schema. The API key travels in the
/// "x-goog-api-key" header, never in the URL, so it cannot end up in a log line.
/// </summary>
public sealed class GeminiReceiptParser(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiReceiptParser> logger)
    : IReceiptParser
{
    public string Provider => "gemini";

    public bool IsDemo => false;

    internal const string Instructions = """
        Sen Türkiye'deki market, eczane, kafe ve mağaza fişlerini okuyan bir asistansın. Görseldeki fişi oku ve yalnızca
        istenen JSON'u döndür.

        Satırlar:
        - Yalnızca satın alınan ürün ve hizmet satırlarını "lines" içine yaz.
        - Şunlar ürün satırı DEĞİLDİR, yazma: TOPLAM, ARA TOPLAM, TOPKDV, KDV, %1/%10/%20 KDV dökümleri, NAKİT,
          KREDİ KARTI, BANKA KARTI, PARA ÜSTÜ, ÖDENEN, kasiyer, kasa/fiş/Z no, ETTN, VKN, TCKN, adres, telefon,
          sadakat kartı, puan ve "TEŞEKKÜR EDERİZ" gibi satırlar.
        - Miktarlı satırlar: "2 X 12,50" ya da "2 AD X 12,50" gibi bir satır, altındaki veya üstündeki ürüne aittir.
          quantity = 2, unit = "adet", amount = satırın toplamı (25,00).
        - Tartılı ürünler: "0,755 KG X 89,90" gibi satırlarda quantity = 0.755, unit = "kg", amount = satır toplamı.
        - Satıra ait indirim ("İNDİRİM", "İND.", "KAMPANYA", "-5,00" gibi) o ürünün tutarından düşülür; indirim ayrı satır
          olarak yazılmaz. Hiçbir ürüne bağlanamayan, fişin geneline uygulanan indirim generalDiscount alanına pozitif
          sayı olarak yazılır.
        - amount her zaman pozitif, KDV dahil, satırın ödenen toplamıdır.

        Ürün adı:
        - rawText satırın fişte yazdığı haliyle kalır.
        - name, kısaltmaları açılmış, büyük-küçük harfi düzgün, anlaşılır Türkçe ürün adıdır:
          "SUT YRM YGL 1L" -> "Yarım yağlı süt 1 L", "EKMK" -> "Ekmek", "DTRJN BLSK" -> "Bulaşık deterjanı",
          "YMRT 15LI" -> "Yumurta (15'li)". Marka adını koru. Emin olamadığın satırda rawText'i düzgün yazılmış
          haliyle kullan; uydurma.
        - Miktarı adın içine yazma.

        Kategori: Her satır için category alanına verilen listeden en uygun gider kategorisini seç. Gıda ve temizlik
        malzemesi Market'tir; kafede içilen kahve Kafe/Kahve, restoranda yenen yemek Dışarıda Yemek'tir. Uymuyorsa
        Diğer Gider.

        Sayılar ve tarih:
        - Fişte sayılar Türk biçimindedir (1.234,56): JSON'a noktalı ondalıkla yaz (1234.56).
        - Tarih fişte gg.aa.yyyy, gg/aa/yyyy ya da gg-aa-yyyy olur; date alanına yyyy-MM-dd olarak yaz. Okunmuyorsa null.
        - total, fişin TOPLAM satırındaki ödenen tutardır; okunmuyorsa null.

        Kişisel bilgi: Kart numarası, isim, TCKN, adres, telefon gibi bilgileri hiçbir alana yazma.

        Görselde fiş yoksa ya da fiş okunamayacak kadar bulanıksa isReceipt = false ve lines = [] döndür.
        """;

    private const string UserPrompt = "Bu fişi oku.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReceiptParseResult> ParseAsync(ReceiptImage image, IReadOnlyList<string> expenseCategories, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new ReceiptParseException(ReceiptErrorKind.Misconfigured, "Gemini:ApiKey is not set.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{settings.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(settings.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        request.Content = new StringContent(
            BuildRequestBody(image, expenseCategories, settings).ToJsonString(), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

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
            throw new ReceiptParseException(ReceiptErrorKind.Timeout, $"No answer within {settings.TimeoutSeconds} s.");
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning(e, "Gemini could not be reached.");
            throw new ReceiptParseException(ReceiptErrorKind.ServiceUnavailable, e.Message);
        }

        if ((int)status is < 200 or > 299)
        {
            var error = FromStatus(status, body);
            // The body of an error holds Google's explanation, never the picture or the key.
            logger.LogWarning("Gemini answered {Status}: {Kind}. {Body}", (int)status, error.Kind, Truncate(body));
            throw error;
        }

        try
        {
            return ReadAnswer(body);
        }
        catch (ReceiptParseException e)
        {
            logger.LogWarning("Gemini's answer was not usable: {Kind}. {Detail}", e.Kind, e.Detail);
            throw;
        }
    }

    // ---------- request ----------

    internal static JsonObject BuildRequestBody(ReceiptImage image, IReadOnlyList<string> expenseCategories, GeminiOptions settings)
    {
        var generationConfig = new JsonObject
        {
            ["temperature"] = 0,
            ["responseMimeType"] = "application/json",
            ["responseJsonSchema"] = Schema(expenseCategories)
        };
        if (!string.IsNullOrWhiteSpace(settings.ThinkingLevel))
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = settings.ThinkingLevel };

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = Instructions })
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = image.MimeType,
                            ["data"] = Convert.ToBase64String(image.Data)
                        }
                    },
                    new JsonObject { ["text"] = UserPrompt })
            }),
            ["generationConfig"] = generationConfig
        };
    }

    /// <summary>The shape of the answer. The category is an enum of the user's expense categories, so the model
    /// cannot invent one.</summary>
    internal static JsonObject Schema(IReadOnlyList<string> expenseCategories)
    {
        static JsonObject Nullable(string type, string description) => new()
        {
            ["type"] = new JsonArray(type, "null"),
            ["description"] = description
        };

        var line = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["rawText"] = new JsonObject { ["type"] = "string", ["description"] = "Satırın fişte yazdığı hali." },
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Anlaşılır Türkçe ürün adı." },
                ["quantity"] = Nullable("number", "Miktar; yazmıyorsa null."),
                ["unit"] = Nullable("string", "adet, kg, g, lt; yazmıyorsa null."),
                ["amount"] = new JsonObject { ["type"] = "number", ["description"] = "Satırın KDV dahil ödenen toplamı." },
                ["category"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(expenseCategories.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray())
                }
            },
            ["required"] = new JsonArray("rawText", "name", "quantity", "unit", "amount", "category")
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["isReceipt"] = new JsonObject { ["type"] = "boolean" },
                ["storeName"] = Nullable("string", "İşletmenin adı."),
                ["date"] = Nullable("string", "Fiş tarihi, yyyy-MM-dd."),
                ["total"] = Nullable("number", "TOPLAM satırındaki tutar."),
                ["generalDiscount"] = Nullable("number", "Hiçbir ürüne bağlanamayan genel indirim, pozitif."),
                ["lines"] = new JsonObject { ["type"] = "array", ["items"] = line }
            },
            ["required"] = new JsonArray("isReceipt", "storeName", "date", "total", "generalDiscount", "lines")
        };
    }

    // ---------- response ----------

    private static ReceiptParseException FromStatus(HttpStatusCode status, string body) => (int)status switch
    {
        // Gemini answers 429 both for "too many per minute" and for "today's quota is gone"; the text tells them apart.
        429 when body.Contains("per day", StringComparison.OrdinalIgnoreCase)
                 || body.Contains("PerDay", StringComparison.Ordinal)
                 || body.Contains("quota_exceeded", StringComparison.OrdinalIgnoreCase)
            => new(ReceiptErrorKind.QuotaExceeded, "429: daily quota"),
        429 => new(ReceiptErrorKind.RateLimited, "429"),
        408 or 504 => new(ReceiptErrorKind.Timeout, $"{(int)status}"),
        400 or 401 or 402 or 403 or 404 => new(ReceiptErrorKind.Misconfigured, $"{(int)status}"),
        _ => new(ReceiptErrorKind.ServiceUnavailable, $"{(int)status}")
    };

    internal static ReceiptParseResult ReadAnswer(string body)
    {
        GeminiResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<GeminiResponse>(body, Json);
        }
        catch (JsonException e)
        {
            throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "Envelope is not JSON: " + e.Message);
        }

        var candidate = response?.Candidates?.FirstOrDefault();
        if (candidate is null)
        {
            if (response?.PromptFeedback?.BlockReason is { } blockReason)
                throw new ReceiptParseException(ReceiptErrorKind.Blocked, "Prompt blocked: " + blockReason);
            throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "No candidates.");
        }

        switch (candidate.FinishReason)
        {
            case "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "IMAGE_SAFETY" or "SPII" or "RECITATION":
                throw new ReceiptParseException(ReceiptErrorKind.Blocked, "Finish reason: " + candidate.FinishReason);
            case "MAX_TOKENS":
                throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "The answer was cut off (MAX_TOKENS).");
        }

        // Thought summaries, if any, are not part of the answer.
        var text = string.Concat((candidate.Content?.Parts ?? []).Where(p => p.Thought != true).Select(p => p.Text));
        if (string.IsNullOrWhiteSpace(text))
            throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "Empty answer.");

        ReceiptJson? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<ReceiptJson>(text, Json);
        }
        catch (JsonException e)
        {
            throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "Answer is not valid JSON: " + e.Message);
        }
        if (receipt is null)
            throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "Answer is null.");

        return ToResult(receipt);
    }

    private static ReceiptParseResult ToResult(ReceiptJson receipt)
    {
        var lines = new List<ReceiptLine>();
        var discount = Math.Max(0m, receipt.GeneralDiscount ?? 0m);
        foreach (var line in receipt.Lines ?? [])
        {
            if (line.Amount is not { } amount)
                throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "A line has no amount.");

            // The instructions say discounts belong to their product; one that still comes as its own (negative)
            // line is treated as a discount on the whole receipt.
            if (amount < 0)
            {
                discount += -amount;
                continue;
            }

            var raw = line.RawText?.Trim() ?? "";
            var name = string.IsNullOrWhiteSpace(line.Name) ? raw : line.Name.Trim();
            if (name.Length == 0)
                throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "A line has no text.");

            lines.Add(new ReceiptLine(raw, name, line.Quantity, string.IsNullOrWhiteSpace(line.Unit) ? null : line.Unit.Trim(),
                amount, line.Category));
        }

        DateTime? date = DateTime.TryParseExact(receipt.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;
        var store = string.IsNullOrWhiteSpace(receipt.StoreName) ? null : receipt.StoreName.Trim();

        return new ReceiptParseResult(receipt.IsReceipt ?? lines.Count > 0, store, date, receipt.Total, discount, lines);
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500] + "…";

    private sealed class GeminiResponse
    {
        public List<Candidate>? Candidates { get; set; }
        public Feedback? PromptFeedback { get; set; }
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

    private sealed class ReceiptJson
    {
        public bool? IsReceipt { get; set; }
        public string? StoreName { get; set; }
        public string? Date { get; set; }
        public decimal? Total { get; set; }
        public decimal? GeneralDiscount { get; set; }
        public List<LineJson>? Lines { get; set; }
    }

    private sealed class LineJson
    {
        public string? RawText { get; set; }
        public string? Name { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? Amount { get; set; }
        public string? Category { get; set; }
    }
}
