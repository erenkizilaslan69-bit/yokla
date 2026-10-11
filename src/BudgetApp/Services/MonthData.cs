using System.Globalization;
using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>A transaction joined with the category flags the calculation services need.</summary>
internal record MonthRow(
    int Id, DateTime Date, TransactionType Type, decimal Amount,
    int CategoryId, string CategoryName, bool IsFixed, bool IsUnnecessary, string? ItemName, bool IsRecurring)
{
    public bool IsExpense => Type == TransactionType.Expense;

    /// <summary>
    /// Known once-a-month amounts: fixed categories (rent, bills) and anything generated from a recurring definition,
    /// whatever its category. These are taken as they are instead of being extrapolated to the end of the month.
    /// </summary>
    public bool IsFixedLike => IsFixed || IsRecurring;
}

internal static class MonthData
{
    /// <summary>The user's transactions with <c>start &lt;= Date &lt; endExclusive</c>.</summary>
    public static async Task<List<MonthRow>> GetRowsAsync(
        ApplicationDbContext db, string userId, DateTime start, DateTime endExclusive)
    {
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < endExclusive)
            .Select(t => new
            {
                t.Id, t.Date, t.Type, t.Amount, t.CategoryId,
                CategoryName = t.Category!.Name, t.Category.IsFixed, t.Category.IsUnnecessary, t.ItemName,
                // RecurringPeriod (not the link) so the row stays "recurring" after its definition is deleted.
                IsRecurring = t.RecurringPeriod != null
            })
            .ToListAsync();

        return rows
            .Select(r => new MonthRow(r.Id, r.Date, r.Type, r.Amount, r.CategoryId, r.CategoryName,
                r.IsFixed, r.IsUnnecessary, r.ItemName, r.IsRecurring))
            .ToList();
    }

    public static Task<List<MonthRow>> GetMonthAsync(ApplicationDbContext db, string userId, int year, int month)
    {
        var start = new DateTime(year, month, 1);
        return GetRowsAsync(db, userId, start, start.AddMonths(1));
    }

    // Amounts are summed in memory: SQLite cannot aggregate decimal columns reliably.
    public static decimal Income(IEnumerable<MonthRow> rows) =>
        rows.Where(r => !r.IsExpense).Sum(r => r.Amount);

    public static decimal Expense(IEnumerable<MonthRow> rows) =>
        rows.Where(r => r.IsExpense).Sum(r => r.Amount);
}

/// <summary>Turkish (tr-TR) formatting for user-facing sentences produced by services.</summary>
internal static class Tr
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");

    public static string Money(decimal value) => value.ToString("N2", Culture) + " TL";

    public static string MonthYear(DateTime date) => date.ToString("MMMM yyyy", Culture);

    public static int Percent(decimal ratio) => (int)Math.Round(ratio * 100m, MidpointRounding.AwayFromZero);
}
