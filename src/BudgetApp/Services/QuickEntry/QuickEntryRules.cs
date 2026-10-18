using BudgetApp.Localization;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>A line ready for saving or for the confirmation screen.</summary>
/// <param name="Amount">Null when no amount was heard: the user has to type it.</param>
/// <param name="CategoryId">Always a category of the line's type (the catch-all one when the reader's was unusable).</param>
/// <param name="Problem">Why the line needs a look, in Turkish, or null.</param>
public sealed record QuickEntryLine(string Description, decimal? Amount, TransactionType Type, int CategoryId, DateTime Date, string? Problem);

/// <summary>What came out of one quick entry.</summary>
/// <param name="Heard">The sentence as heard or typed, shown to the user.</param>
/// <param name="AutoSave">True when every line is clear enough to be saved without asking.</param>
public sealed record QuickEntryProposal(string Heard, IReadOnlyList<QuickEntryLine> Lines, bool AutoSave, bool IsDemo);

/// <summary>
/// Checks what the reader understood. The reader's own confidence is not a measurement, so a line is saved without
/// asking only when it is also plainly right: amount, matching category, a recent date, nothing the reader doubted.
/// See docs/rules.md, "Söyle ya da yaz".
/// </summary>
public static class QuickEntryRules
{
    /// <summary>The note every quick entry transaction gets: "Geri al" and "Düzenle" only touch these.</summary>
    public const string Note = "Hızlı giriş";

    /// <summary>More lines than this always go to the confirmation screen.</summary>
    public const int AutoSaveMaxLines = 8;

    /// <summary>A date further back than this always goes to the confirmation screen.</summary>
    public const int AutoSaveMaxAgeDays = 7;

    /// <summary>A date further back than this is pointed out on the confirmation screen.</summary>
    public const int OldDateDays = 60;

    public const string FallbackExpense = "Diğer Gider";
    public const string FallbackIncome = "Diğer Gelir";

    /// <summary>
    /// Turns the reader's items into lines: amounts rounded to the kuruş, a category of the right type, a date that is
    /// not in the future, and a note on every line that needs a look.
    /// </summary>
    public static List<QuickEntryLine> Resolve(IReadOnlyList<QuickEntryItem> items, IReadOnlyList<Category> categories, DateTime today)
    {
        today = today.Date;
        var byName = categories
            .GroupBy(c => (c.Type, Name: c.Name.ToLower(Tr.Culture)))
            .ToDictionary(g => g.Key, g => g.First().Id);

        int Fallback(TransactionType type)
        {
            var name = type == TransactionType.Income ? FallbackIncome : FallbackExpense;
            return categories.FirstOrDefault(c => c.Type == type && c.Name == name)?.Id
                   ?? categories.Last(c => c.Type == type).Id;
        }

        return items.Select(item =>
        {
            var problems = new List<string>();
            // The reader's own words about what it doubted come first; the rules' sentences only fill in where it said nothing.
            var issue = string.IsNullOrWhiteSpace(item.Issue) ? null : item.Issue.Trim();
            if (issue is not null)
                problems.Add(issue);

            decimal? amount = item.Amount is > 0 and var a ? MoneyMath.Round(a) : null;
            if (amount is null or 0m)
            {
                amount = null;
                if (issue is null)
                    problems.Add("Tutarı duyamadım, yazar mısın?");
            }

            int categoryId;
            if (item.Category is { } name && byName.TryGetValue((item.Type, name.Trim().ToLower(Tr.Culture)), out var id))
                categoryId = id;
            else
            {
                categoryId = Fallback(item.Type);
                problems.Add("Kategoriyi seç.");
            }

            var date = item.Date?.Date ?? today;
            if (date > today)
            {
                problems.Add($"İleri bir tarih anlaşıldı ({date:dd.MM.yyyy}); bugünü koydum.");
                date = today;
            }
            else if (date < today.AddDays(-OldDateDays))
                problems.Add($"Tarih {date:dd.MM.yyyy}, {OldDateDays} günden eski. Doğru mu?");

            if (item.Confidence != QuickEntryConfidence.High && problems.Count == 0)
                problems.Add("Emin olamadım, kontrol et.");

            var description = Describe(item.Description, categories.First(c => c.Id == categoryId).Name);
            return new QuickEntryLine(description, amount, item.Type, categoryId, date, problems.Count == 0 ? null : string.Join(" ", problems));
        }).ToList();
    }

    /// <summary>
    /// True when the lines can be saved straight away: something was heard, 1 to <see cref="AutoSaveMaxLines"/> lines,
    /// each with high confidence, an amount, a category the reader chose itself, no doubt raised and a date within the
    /// last <see cref="AutoSaveMaxAgeDays"/> days.
    /// </summary>
    public static bool CanAutoSave(string heard, IReadOnlyList<QuickEntryItem> items, IReadOnlyList<QuickEntryLine> lines, DateTime today) =>
        !string.IsNullOrWhiteSpace(heard)
        && lines.Count is >= 1 and <= AutoSaveMaxLines
        && items.Count == lines.Count
        && items.All(i => i.Confidence == QuickEntryConfidence.High)
        && lines.All(l => l.Problem is null && l.Amount is > 0 && l.Date >= today.Date.AddDays(-AutoSaveMaxAgeDays) && l.Date <= today.Date);

    /// <summary>A short name for the line: the reader's, first letter capitalised, or the category's.</summary>
    internal static string Describe(string? description, string categoryName)
    {
        var text = description?.Trim();
        if (string.IsNullOrEmpty(text))
            return categoryName;
        text = ReceiptMath.Shorten(text, 200);
        return char.ToUpper(text[0], Tr.Culture) + text[1..];
    }

    /// <summary>"Simit ₺20,00 · Otobüs ₺15,00 · Market ₺230,00", for the message after saving.</summary>
    public static string Summary(IEnumerable<Transaction> saved) =>
        string.Join(" · ", saved.Select(t => $"{t.ItemName} {(t.Type == TransactionType.Income ? "+" : "")}{AppCulture.Money(t.Amount)}"));
}
