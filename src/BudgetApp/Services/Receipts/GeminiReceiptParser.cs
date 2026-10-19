using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace BudgetApp.Services;

/// <summary>
/// Reads receipts with the Google Gemini API through <see cref="GeminiClient"/> (key in a header, answer constrained
/// to a JSON schema, duration/status/tokens logged).
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
        var call = new GeminiRequest(Instructions, new GeminiMedia(image.Data, image.MimeType), UserPrompt,
            Schema(expenseCategories), $"image {image.Data.Length / 1024} KB");

        string answer;
        try
        {
            answer = await new GeminiClient(http, settings, logger).GenerateAsync(call, cancellationToken);
        }
        catch (GeminiCallException e)
        {
            if (e.Failure is GeminiFailure.Blocked or GeminiFailure.InvalidResponse)
                logger.LogWarning("Gemini's answer was not usable: {Kind}. {Detail}", e.Failure, e.Message);
            throw new ReceiptParseException(KindOf(e.Failure), e.Message);
        }

        try
        {
            return ToResult(ReadAnswer(answer));
        }
        catch (ReceiptParseException e)
        {
            logger.LogWarning("Gemini's answer was not usable: {Kind}. {Detail}", e.Kind, e.Detail);
            throw;
        }
    }

    internal static ReceiptErrorKind KindOf(GeminiFailure failure) => failure switch
    {
        GeminiFailure.Misconfigured => ReceiptErrorKind.Misconfigured,
        GeminiFailure.Timeout => ReceiptErrorKind.Timeout,
        GeminiFailure.RateLimited => ReceiptErrorKind.RateLimited,
        GeminiFailure.QuotaExceeded => ReceiptErrorKind.QuotaExceeded,
        GeminiFailure.Blocked => ReceiptErrorKind.Blocked,
        GeminiFailure.InvalidResponse => ReceiptErrorKind.InvalidResponse,
        GeminiFailure.Overloaded => ReceiptErrorKind.Overloaded,
        _ => ReceiptErrorKind.ServiceUnavailable
    };

    // ---------- request ----------

    /// <summary>The whole request body, as <see cref="GeminiClient"/> sends it.</summary>
    internal static JsonObject BuildRequestBody(ReceiptImage image, IReadOnlyList<string> expenseCategories, GeminiOptions settings) =>
        GeminiClient.BuildRequestBody(
            new GeminiRequest(Instructions, new GeminiMedia(image.Data, image.MimeType), UserPrompt, Schema(expenseCategories), ""),
            settings);

    /// <summary>The shape of the answer. The category is an enum of the user's expense categories, so the model
    /// cannot invent one.</summary>
    internal static JsonObject Schema(IReadOnlyList<string> expenseCategories)
    {
        var line = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["rawText"] = new JsonObject { ["type"] = "string", ["description"] = "Satırın fişte yazdığı hali." },
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Anlaşılır Türkçe ürün adı." },
                ["quantity"] = GeminiClient.Nullable("number", "Miktar; yazmıyorsa null."),
                ["unit"] = GeminiClient.Nullable("string", "adet, kg, g, lt; yazmıyorsa null."),
                ["amount"] = new JsonObject { ["type"] = "number", ["description"] = "Satırın KDV dahil ödenen toplamı." },
                ["category"] = new JsonObject { ["type"] = "string", ["enum"] = GeminiClient.Enum(expenseCategories) }
            },
            ["required"] = new JsonArray("rawText", "name", "quantity", "unit", "amount", "category")
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["isReceipt"] = new JsonObject { ["type"] = "boolean" },
                ["storeName"] = GeminiClient.Nullable("string", "İşletmenin adı."),
                ["date"] = GeminiClient.Nullable("string", "Fiş tarihi, yyyy-MM-dd."),
                ["total"] = GeminiClient.Nullable("number", "TOPLAM satırındaki tutar."),
                ["generalDiscount"] = GeminiClient.Nullable("number", "Hiçbir ürüne bağlanamayan genel indirim, pozitif."),
                ["lines"] = new JsonObject { ["type"] = "array", ["items"] = line }
            },
            ["required"] = new JsonArray("isReceipt", "storeName", "date", "total", "generalDiscount", "lines")
        };
    }

    // ---------- response ----------

    private static ReceiptJson ReadAnswer(string text)
    {
        ReceiptJson? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<ReceiptJson>(text, Json);
        }
        catch (JsonException e)
        {
            throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "Answer is not valid JSON: " + e.Message);
        }
        return receipt ?? throw new ReceiptParseException(ReceiptErrorKind.InvalidResponse, "Answer is null.");
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
