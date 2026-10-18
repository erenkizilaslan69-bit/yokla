using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>One line of a confirmation screen (receipt, quick entry) as the user sent it back.</summary>
/// <param name="Date">Only quick entry has a date per line; a receipt's date is on the whole receipt.</param>
public sealed record ReviewRow(bool Include, string? Name, decimal? Amount, int CategoryId, DateTime? Date = null);

/// <summary>What is wrong with a line: <paramref name="Line"/> is its index, or -1 for the form as a whole.</summary>
/// <param name="Field">The row's property name (Name, Amount, CategoryId, Date); empty for the form.</param>
public sealed record ReviewError(int Line, string Field, string Message);

/// <param name="NameMissing">The sentence for a line without a name ("Ürün adını yaz.", "Açıklama yaz.").</param>
/// <param name="OnlyType">When set, a line's category must be of this type (a receipt holds only expenses).</param>
/// <param name="DatePerLine">Every line needs its own date, not in the future.</param>
public sealed record ReviewRules(string NameMissing, string CategoryMissing, TransactionType? OnlyType, bool DatePerLine);

/// <summary>The ticked lines that passed, each with its category.</summary>
public sealed record ReviewResult(IReadOnlyList<ReviewError> Errors, IReadOnlyList<(ReviewRow Row, Category Category)> Chosen)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Checks the lines of a confirmation screen before they are saved. The services check again when saving (amount,
/// date, category type); this gives each line its own message on the screen.
/// </summary>
public static class ReviewLines
{
    public static readonly ReviewRules Receipt = new("Ürün adını yaz.", "Bir gider kategorisi seç.", TransactionType.Expense, DatePerLine: false);

    public static readonly ReviewRules QuickEntry = new("Açıklama yaz.", "Bir kategori seç.", OnlyType: null, DatePerLine: true);

    public static ReviewResult Check(IReadOnlyList<ReviewRow> rows, IReadOnlyCollection<Category> categories, ReviewRules rules, DateTime today)
    {
        var byId = categories.ToDictionary(c => c.Id);
        var errors = new List<ReviewError>();
        var chosen = new List<(ReviewRow, Category)>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!row.Include)
                continue;

            var before = errors.Count;
            if (string.IsNullOrWhiteSpace(row.Name))
                errors.Add(new(i, nameof(ReviewRow.Name), rules.NameMissing));
            if (row.Amount is not { } amount || MoneyMath.Round(amount) <= 0)
                errors.Add(new(i, nameof(ReviewRow.Amount), "Tutar 0'dan büyük olmalı."));
            if (!byId.TryGetValue(row.CategoryId, out var category) || (rules.OnlyType is { } type && category.Type != type))
                errors.Add(new(i, nameof(ReviewRow.CategoryId), rules.CategoryMissing));
            if (rules.DatePerLine)
            {
                if (row.Date is not { } date)
                    errors.Add(new(i, nameof(ReviewRow.Date), "Tarihi gir."));
                else if (date.Date > today.Date)
                    errors.Add(new(i, nameof(ReviewRow.Date), "Tarih ileride olamaz."));
            }

            if (errors.Count == before)
                chosen.Add((row, category!));
        }

        if (rows.All(r => !r.Include))
            errors.Add(new(-1, "", "Kaydetmek için en az bir satır seç."));

        return new ReviewResult(errors, chosen);
    }
}
