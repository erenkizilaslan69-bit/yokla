using System.Globalization;
using System.Text.RegularExpressions;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>
/// Development stand-in for the real reader when no API key is set. Typed text is split with a few simple rules
/// ("simitçiye 20, otobüse 15, markete 230" works; number words do not). A recording is not listened to: it always
/// comes back as <see cref="SampleSentence"/>.
/// </summary>
public sealed partial class DemoQuickEntryParser(TimeProvider? timeProvider = null) : IQuickEntryParser
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string Provider => "demo";

    public bool IsDemo => true;

    public const string SampleSentence = "Simitçiye 20, otobüse 15, markete 230.";

    public Task<QuickEntryParseResult> ParseAsync(QuickEntryInput input, QuickEntryContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Parse(input.Audio is null ? input.Text ?? "" : SampleSentence, context.Today));

    /// <summary>Keyword (start of a word) → category, and the name a line gets when the keyword is all there is.</summary>
    private static readonly (string Stem, string Category, string? Label, TransactionType Type)[] Keywords =
    [
        ("simit", "Dışarıda Yemek", "Simit", TransactionType.Expense),
        ("börek", "Dışarıda Yemek", "Börek", TransactionType.Expense),
        ("döner", "Dışarıda Yemek", "Döner", TransactionType.Expense),
        ("lokanta", "Dışarıda Yemek", "Lokanta", TransactionType.Expense),
        ("yemek", "Dışarıda Yemek", "Yemek", TransactionType.Expense),
        ("tost", "Dışarıda Yemek", "Tost", TransactionType.Expense),
        ("kahve", "Kafe/Kahve", "Kahve", TransactionType.Expense),
        ("çay", "Kafe/Kahve", "Çay", TransactionType.Expense),
        ("kafe", "Kafe/Kahve", "Kafe", TransactionType.Expense),
        ("otobüs", "Ulaşım", "Otobüs", TransactionType.Expense),
        ("metro", "Ulaşım", "Metro", TransactionType.Expense),
        ("dolmuş", "Ulaşım", "Dolmuş", TransactionType.Expense),
        ("minibüs", "Ulaşım", "Minibüs", TransactionType.Expense),
        ("taksi", "Ulaşım", "Taksi", TransactionType.Expense),
        ("benzin", "Ulaşım", "Benzin", TransactionType.Expense),
        ("market", "Market", "Market alışverişi", TransactionType.Expense),
        ("bakkal", "Market", "Bakkal", TransactionType.Expense),
        ("manav", "Market", "Manav", TransactionType.Expense),
        ("fatura", "Faturalar", "Fatura", TransactionType.Expense),
        ("elektrik", "Faturalar", "Elektrik faturası", TransactionType.Expense),
        ("internet", "Faturalar", "İnternet faturası", TransactionType.Expense),
        ("eczane", "Sağlık", "Eczane", TransactionType.Expense),
        ("ilaç", "Sağlık", "İlaç", TransactionType.Expense),
        ("kitap", "Eğitim/Kırtasiye", "Kitap", TransactionType.Expense),
        ("fotokopi", "Eğitim/Kırtasiye", "Fotokopi", TransactionType.Expense),
        ("kırtasiye", "Eğitim/Kırtasiye", "Kırtasiye", TransactionType.Expense),
        ("sinema", "Eğlence", "Sinema", TransactionType.Expense),
        ("konser", "Eğlence", "Konser", TransactionType.Expense),
        ("kira", "Kira/Yurt", "Kira", TransactionType.Expense),
        ("yurt", "Kira/Yurt", "Yurt", TransactionType.Expense),
        ("maaş", "Maaş", "Maaş", TransactionType.Income),
        ("burs", "KYK Bursu", "Burs", TransactionType.Income),
        ("kyk", "KYK Bursu", "Burs", TransactionType.Income),
        ("harçlık", "Aile Desteği", "Harçlık", TransactionType.Income),
        ("annem", "Aile Desteği", "Annemden", TransactionType.Income),
        ("babam", "Aile Desteği", "Babamdan", TransactionType.Income)
    ];

    /// <summary>Words that say "money came in" when no keyword names the income.</summary>
    private static readonly string[] IncomeWords = ["geldi", "yattı", "kazandım", "sattım"];

    /// <summary>Words that are not part of a line's name.</summary>
    private static readonly HashSet<string> FillerWords =
        ["lira", "tl", "kuruş", "verdim", "ödedim", "harcadım", "aldım", "bugün", "dün", "önceki", "evvelki", "gün",
         "geldi", "yattı", "kazandım", "sattım", "için", "da", "de"];

    /// <summary>Splits on commas (not decimal commas), semicolons, new lines and "ve".</summary>
    [GeneratedRegex(@"(?<!\d),|,(?!\d)|;|\n|\s+ve\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Separators();

    /// <summary>"1.250", "1.250,50", "12,5", "20".</summary>
    [GeneratedRegex(@"\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?")]
    private static partial Regex Number();

    [GeneratedRegex(@"[\p{L}\d]+")]
    private static partial Regex Words();

    public static QuickEntryParseResult Parse(string text, DateTime today)
    {
        var heard = text.Trim();
        var lower = heard.ToLower(Tr.Culture);
        var words = Words().Matches(lower).Select(m => m.Value).ToList();
        int? dayShift = words.Contains("dün") ? -1
            : words.Contains("önceki") || words.Contains("evvelki") ? -2
            : null;

        var items = new List<QuickEntryItem>();
        foreach (var part in Separators().Split(heard))
        {
            var chunk = part.Trim().TrimEnd('.', '!');
            if (chunk.Length == 0)
                continue;

            var number = Number().Match(chunk);
            decimal? amount = number.Success ? ParseAmount(number.Value) : null;
            var rest = (number.Success ? chunk.Remove(number.Index, number.Length) : chunk).ToLower(Tr.Culture);
            var restWords = Words().Matches(rest).Select(m => m.Value).Where(w => !FillerWords.Contains(w)).ToList();

            var keyword = Keywords.FirstOrDefault(k => restWords.Any(w => w.StartsWith(k.Stem, StringComparison.Ordinal)));
            var incomeWord = IncomeWords.Any(w => rest.Contains(w, StringComparison.Ordinal));
            if (amount is null && keyword.Stem is null)
                continue; // neither money nor anything we know: not a line

            var type = keyword.Stem is not null ? keyword.Type : incomeWord ? TransactionType.Income : TransactionType.Expense;
            var category = keyword.Category ?? (incomeWord ? QuickEntryRules.FallbackIncome : null);
            var description = keyword.Label ?? QuickEntryRules.Describe(string.Join(" ", restWords.Select(StripCase)), "");

            var confidence = amount is null ? QuickEntryConfidence.Low
                : keyword.Stem is null ? QuickEntryConfidence.Medium
                : QuickEntryConfidence.High;
            var issue = amount is null ? "Tutarı anlayamadım." : null;
            var date = dayShift is { } shift ? today.Date.AddDays(shift) : (DateTime?)null;

            items.Add(new QuickEntryItem(description, amount, type, category, date, confidence, issue));
        }

        return new QuickEntryParseResult(heard, items.Count > 0, items);
    }

    private static decimal? ParseAmount(string text)
    {
        // Turkish: dots group thousands, a comma marks the decimals; "2.5" (one or two digits after a dot) is a decimal.
        var normalized = text.Contains(',') || Regex.IsMatch(text, @"\.\d{3}(?!\d)")
            ? text.Replace(".", "").Replace(',', '.')
            : text;
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }

    /// <summary>"simitçiye" → "simitçi", "markete" → "market": the dative ending of a place.</summary>
    private static string StripCase(string word)
    {
        foreach (var ending in new[] { "'ye", "'ya", "'e", "'a", "ye", "ya" })
            if (word.Length > ending.Length + 2 && word.EndsWith(ending, StringComparison.Ordinal))
                return word[..^ending.Length];
        if (word.Length > 4 && (word.EndsWith('e') || word.EndsWith('a')))
            return word[..^1];
        return word;
    }
}
