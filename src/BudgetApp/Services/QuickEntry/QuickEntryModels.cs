using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>"QuickEntry" section of the configuration ("Söyle ya da yaz").</summary>
public sealed class QuickEntryOptions
{
    public const string Section = "QuickEntry";

    /// <summary>Quick entries (spoken or typed) one user may send to the reader per day. The app's limit is shared
    /// with receipt reading (<see cref="ReceiptOptions.DailyLimitTotal"/>).</summary>
    public int DailyLimitPerUser { get; set; } = 30;

    /// <summary>The browser stops recording after this many seconds.</summary>
    public int MaxRecordingSeconds { get; set; } = 30;

    /// <summary>A recording larger than this is refused (30 s of browser audio is well under 1 MB).</summary>
    public int MaxAudioBytes { get; set; } = 2_000_000;

    public int MaxTextLength { get; set; } = 500;
}

/// <summary>A voice recording held in memory only; it is never written to disk, the database or the log.</summary>
public sealed record QuickEntryAudio(byte[] Data, string MimeType)
{
    /// <summary>
    /// The recording's type for Gemini, from its first bytes (not from the browser's word for it). Chrome and Edge
    /// record WebM/Opus, Firefox Ogg/Opus, Safari MP4/AAC, which goes as "audio/m4a" (the same container; Gemini
    /// lists m4a, not mp4). WAV is accepted too. Null for anything else, including MP4-family pictures (HEIC, AVIF).
    /// </summary>
    public static string? DetectMimeType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
            return "audio/webm";
        if (data.Length >= 4 && data[..4].SequenceEqual("OggS"u8))
            return "audio/ogg";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WAVE"u8))
            return "audio/wav";
        if (data.Length >= 12 && data[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = data[8..12];
            foreach (var picture in PictureBrands)
                if (brand.SequenceEqual(picture))
                    return null;
            return "audio/m4a";
        }
        return null;
    }

    private static readonly byte[][] PictureBrands =
        ["heic"u8.ToArray(), "heix"u8.ToArray(), "hevc"u8.ToArray(), "mif1"u8.ToArray(), "msf1"u8.ToArray(),
         "heif"u8.ToArray(), "avif"u8.ToArray()];
}

/// <summary>What the user said or typed: exactly one of the two.</summary>
public sealed record QuickEntryInput(string? Text, QuickEntryAudio? Audio)
{
    public static QuickEntryInput FromText(string text) => new(text, null);

    public static QuickEntryInput FromAudio(QuickEntryAudio audio) => new(null, audio);
}

public enum QuickEntryConfidence { Low, Medium, High }

/// <summary>One transaction as the reader understood it; nothing here is trusted yet.</summary>
/// <param name="Amount">Null when no amount was heard.</param>
/// <param name="Category">One of the category names the reader was given (or something else if it misbehaved).</param>
/// <param name="Date">Null means today.</param>
/// <param name="Issue">What the reader was unsure about, in Turkish, or null.</param>
public sealed record QuickEntryItem(
    string Description, decimal? Amount, TransactionType Type, string? Category, DateTime? Date,
    QuickEntryConfidence Confidence, string? Issue);

/// <param name="Heard">The sentence as the reader heard (or read) it; shown to the user.</param>
/// <param name="IsEntry">False when there was no money talk at all (silence, noise, an unrelated sentence).</param>
public sealed record QuickEntryParseResult(string Heard, bool IsEntry, IReadOnlyList<QuickEntryItem> Items);

/// <summary>What the reader needs besides the input.</summary>
public sealed record QuickEntryContext(DateTime Today, IReadOnlyList<string> ExpenseCategories, IReadOnlyList<string> IncomeCategories);

/// <summary>Turns a spoken or typed sentence into transactions. The provider behind it is hidden from the rest of the app.</summary>
public interface IQuickEntryParser
{
    /// <summary>Short name stored with each attempt ("gemini", "demo").</summary>
    string Provider { get; }

    /// <summary>A demo reader works without a key; the page says so and the limits do not apply.</summary>
    bool IsDemo { get; }

    /// <exception cref="QuickEntryException">Nothing usable came back; the message can be shown to the user.</exception>
    Task<QuickEntryParseResult> ParseAsync(QuickEntryInput input, QuickEntryContext context, CancellationToken cancellationToken);
}

public enum QuickEntryErrorKind
{
    /// <summary>Quick entry is switched off (no API key outside Development).</summary>
    Disabled,
    Empty,
    TextTooLong,
    InvalidAudio,
    UserDailyLimit,
    TotalDailyLimit,
    Timeout,
    QuotaExceeded,
    RateLimited,
    /// <summary>The reader answered, but heard no transactions in it.</summary>
    NotUnderstood,
    InvalidResponse,
    Blocked,
    Misconfigured,
    ServiceUnavailable,
    /// <summary>The model is overloaded right now (HTTP 503, also after the retries).</summary>
    Overloaded
}

/// <summary>A quick entry could not be read. <see cref="Exception.Message"/> is a Turkish sentence for the user.</summary>
public sealed class QuickEntryException(QuickEntryErrorKind kind, string? detail = null, int? limit = null)
    : Exception(QuickEntryMessages.For(kind, limit))
{
    public QuickEntryErrorKind Kind { get; } = kind;

    /// <summary>Technical detail for the log; never shown to the user.</summary>
    public string? Detail { get; } = detail;

    public static QuickEntryErrorKind KindOf(GeminiFailure failure) => failure switch
    {
        GeminiFailure.Misconfigured => QuickEntryErrorKind.Misconfigured,
        GeminiFailure.Timeout => QuickEntryErrorKind.Timeout,
        GeminiFailure.RateLimited => QuickEntryErrorKind.RateLimited,
        GeminiFailure.QuotaExceeded => QuickEntryErrorKind.QuotaExceeded,
        GeminiFailure.Blocked => QuickEntryErrorKind.Blocked,
        GeminiFailure.InvalidResponse => QuickEntryErrorKind.InvalidResponse,
        GeminiFailure.Overloaded => QuickEntryErrorKind.Overloaded,
        _ => QuickEntryErrorKind.ServiceUnavailable
    };
}

public static class QuickEntryMessages
{
    public static string For(QuickEntryErrorKind kind, int? limit = null) => kind switch
    {
        QuickEntryErrorKind.Disabled => "Söyle ya da yaz şu an kullanılamıyor.",
        QuickEntryErrorKind.Empty => "Önce harcamanı söyle ya da yaz.",
        QuickEntryErrorKind.TextTooLong => $"Bu kadar uzun bir metni ayıramam; en fazla {limit} karakter yaz.",
        QuickEntryErrorKind.InvalidAudio => "Ses kaydı alınamadı. Tekrar kaydet ya da yazarak gir.",
        QuickEntryErrorKind.UserDailyLimit =>
            $"Bugünkü söyle ya da yaz hakkını kullandın ({limit}/{limit}). Yarın tekrar dene ya da işlemleri elle ekle.",
        QuickEntryErrorKind.TotalDailyLimit =>
            "Söyle ya da yaz bugün herkes için yoğundu ve günlük sınıra ulaştı. Yarın tekrar dene ya da işlemleri elle ekle.",
        QuickEntryErrorKind.Timeout => "Ayırmak uzun sürdü. Biraz sonra tekrar dene.",
        QuickEntryErrorKind.QuotaExceeded => "Servisin bugünkü kotası doldu. Yarın tekrar deneyebilir ya da işlemleri elle ekleyebilirsin.",
        QuickEntryErrorKind.RateLimited => "Kısa sürede çok fazla istek gönderildi. Bir dakika sonra tekrar dene.",
        QuickEntryErrorKind.NotUnderstood =>
            "Söylediğinde bir harcama ya da gelir bulamadım. \"Simitçiye 20, otobüse 15\" gibi tutarlarla tekrar dener misin?",
        QuickEntryErrorKind.InvalidResponse => "Bir sorun oldu, sonuç anlaşılamadı. Tekrar dene.",
        QuickEntryErrorKind.Blocked => "Bu kayıt işlenemedi. Tekrar dene ya da yazarak gir.",
        QuickEntryErrorKind.Misconfigured => "Söyle ya da yaz şu an kullanılamıyor.",
        QuickEntryErrorKind.ServiceUnavailable => "Servise şu an ulaşılamıyor. Biraz sonra tekrar dene.",
        QuickEntryErrorKind.Overloaded => "Google'ın servisi şu an çok yoğun. Birkaç dakika sonra tekrar dene ya da işlemleri elle ekle.",
        _ => "Söylediğin ayrılamadı."
    };
}
