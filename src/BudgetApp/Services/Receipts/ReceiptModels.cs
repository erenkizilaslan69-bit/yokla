namespace BudgetApp.Services;

/// <summary>A receipt picture held in memory only; it is never written to disk or the database.</summary>
public sealed record ReceiptImage(byte[] Data, string MimeType)
{
    /// <summary>
    /// The picture's type from its first bytes (not from the file name or the browser's word for it):
    /// JPEG, PNG, WebP or HEIC/HEIF, the image types the reader accepts. Null for anything else.
    /// </summary>
    public static string? DetectMimeType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        if (data.Length >= 12 && data[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = data[8..12];
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8))
                return "image/heic";
            if (brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8) || brand.SequenceEqual("heif"u8))
                return "image/heif";
        }
        return null;
    }
}

/// <summary>One product line as the reader understood it.</summary>
/// <param name="RawText">The line as printed, e.g. "SUT YRM YGL 1L".</param>
/// <param name="Name">A readable product name, e.g. "Yarım yağlı süt 1 L".</param>
/// <param name="Amount">What the line cost in total, after any discount printed for that line.</param>
/// <param name="Category">The suggested expense category, one of the names the reader was given.</param>
public sealed record ReceiptLine(string RawText, string Name, decimal? Quantity, string? Unit, decimal Amount, string? Category);

/// <summary>What the reader got out of a picture.</summary>
/// <param name="IsReceipt">False when the picture is not a receipt at all.</param>
/// <param name="GeneralDiscount">A discount on the whole receipt that belongs to no single line (positive amount).</param>
public sealed record ReceiptParseResult(
    bool IsReceipt, string? StoreName, DateTime? Date, decimal? Total, decimal GeneralDiscount,
    IReadOnlyList<ReceiptLine> Lines);

/// <summary>Reads receipts from pictures. The provider behind it (Gemini, a demo) is hidden from the rest of the app.</summary>
public interface IReceiptParser
{
    /// <summary>Short name stored with each attempt ("gemini", "demo").</summary>
    string Provider { get; }

    /// <summary>A demo reader returns ready-made results; the page says so and the limits do not apply.</summary>
    bool IsDemo { get; }

    /// <param name="expenseCategories">The names a line's category has to be chosen from.</param>
    /// <exception cref="ReceiptParseException">The receipt could not be read; the message can be shown to the user.</exception>
    Task<ReceiptParseResult> ParseAsync(ReceiptImage image, IReadOnlyList<string> expenseCategories, CancellationToken cancellationToken);
}

public enum ReceiptErrorKind
{
    /// <summary>Receipt reading is switched off (no API key outside Development).</summary>
    Disabled,
    InvalidFile,
    UserDailyLimit,
    TotalDailyLimit,
    Timeout,
    /// <summary>The provider's daily quota is used up.</summary>
    QuotaExceeded,
    /// <summary>Too many requests in a short time at the provider.</summary>
    RateLimited,
    /// <summary>The picture holds no readable receipt.</summary>
    Unreadable,
    /// <summary>The answer could not be understood (not JSON, wrong shape, cut off).</summary>
    InvalidResponse,
    /// <summary>The provider refused to process the picture (safety filters).</summary>
    Blocked,
    /// <summary>A problem on our side: wrong key, unknown model, bad request. Details go to the log only.</summary>
    Misconfigured,
    /// <summary>The provider is down or failed.</summary>
    ServiceUnavailable,
    /// <summary>The model is overloaded right now (HTTP 503, also after the retries).</summary>
    Overloaded
}

/// <summary>A receipt could not be read. <see cref="Exception.Message"/> is a Turkish sentence for the user.</summary>
public sealed class ReceiptParseException(ReceiptErrorKind kind, string? detail = null, int? limit = null)
    : Exception(ReceiptMessages.For(kind, limit))
{
    public ReceiptErrorKind Kind { get; } = kind;

    /// <summary>Technical detail for the log; never shown to the user.</summary>
    public string? Detail { get; } = detail;
}

public static class ReceiptMessages
{
    public static string For(ReceiptErrorKind kind, int? limit = null) => kind switch
    {
        ReceiptErrorKind.Disabled => "Fiş okuma şu an kullanılamıyor.",
        ReceiptErrorKind.InvalidFile => "Bu dosya kabul edilmedi: JPEG, PNG, WebP veya HEIC bir fotoğraf, en fazla 4 MB.",
        ReceiptErrorKind.UserDailyLimit =>
            $"Bugünkü fiş hakkını kullandın ({limit}/{limit}). Yarın tekrar dene ya da işlemleri elle ekle.",
        ReceiptErrorKind.TotalDailyLimit =>
            "Fiş okuma bugün herkes için yoğundu ve günlük sınıra ulaştı. Yarın tekrar dene ya da işlemleri elle ekle.",
        ReceiptErrorKind.Timeout => "Fiş okunması uzun sürdü. Biraz sonra tekrar dene.",
        ReceiptErrorKind.QuotaExceeded => "Fiş okuma servisinin bugünkü kotası doldu. Yarın tekrar deneyebilirsin.",
        ReceiptErrorKind.RateLimited => "Kısa sürede çok fazla fiş gönderildi. Bir dakika sonra tekrar dene.",
        ReceiptErrorKind.Unreadable =>
            "Bu görselde okunabilir bir fiş bulamadım. Fişi düz bir zeminde, yakından ve iyi ışıkta çek.",
        ReceiptErrorKind.InvalidResponse => "Fiş okunurken bir sorun oldu, sonuç anlaşılamadı. Tekrar dene.",
        ReceiptErrorKind.Blocked => "Bu görsel işlenemedi. Başka bir fotoğraf dene.",
        ReceiptErrorKind.Misconfigured => "Fiş okuma şu an kullanılamıyor.",
        ReceiptErrorKind.ServiceUnavailable => "Fiş okuma servisine şu an ulaşılamıyor. Biraz sonra tekrar dene.",
        ReceiptErrorKind.Overloaded => "Google'ın fiş okuma servisi şu an çok yoğun. Birkaç dakika sonra tekrar dene.",
        _ => "Fiş okunamadı."
    };
}
