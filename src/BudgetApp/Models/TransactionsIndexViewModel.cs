using BudgetApp.Services;

namespace BudgetApp.Models;

public class TransactionsIndexViewModel
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required List<Transaction> Transactions { get; init; }
    public required TransactionTotals Totals { get; init; }
    public required bool IsCurrentMonth { get; init; }

    /// <summary>Warnings produced by the transaction that was just saved.</summary>
    public List<string> Warnings { get; init; } = [];

    public MonthPickerViewModel Picker => new()
    {
        Controller = "Transactions", Year = Year, Month = Month, IsCurrentMonth = IsCurrentMonth
    };
}
