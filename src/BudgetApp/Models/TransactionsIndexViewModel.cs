using System.Globalization;
using BudgetApp.Services;

namespace BudgetApp.Models;

public class TransactionsIndexViewModel
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required List<Transaction> Transactions { get; init; }
    public required TransactionTotals Totals { get; init; }

    /// <summary>The month shown is the current one, so there is no "next" month to go to.</summary>
    public required bool IsCurrentMonth { get; init; }

    private DateTime First => new(Year, Month, 1);

    public DateTime Previous => First.AddMonths(-1);
    public DateTime Next => First.AddMonths(1);

    public string Title => First.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR"));
}
