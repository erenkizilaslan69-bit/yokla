using System.Globalization;

namespace BudgetApp.Models;

/// <summary>Previous/next month navigation shared by the transactions and analysis pages.</summary>
public class MonthPickerViewModel
{
    public required string Controller { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }

    /// <summary>The month shown is the current one, so there is no "next" month to go to.</summary>
    public required bool IsCurrentMonth { get; init; }

    private DateTime First => new(Year, Month, 1);

    public DateTime Previous => First.AddMonths(-1);
    public DateTime Next => First.AddMonths(1);

    public string Title => First.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR"));

    /// <summary>Falls back to the current month for missing, invalid or future values.</summary>
    public static (int Year, int Month) Resolve(int? year, int? month, DateTime today)
    {
        var valid = year is >= 2000 && month is >= 1 and <= 12
            && new DateTime(year.Value, month.Value, 1) <= new DateTime(today.Year, today.Month, 1);
        return valid ? (year!.Value, month!.Value) : (today.Year, today.Month);
    }
}
