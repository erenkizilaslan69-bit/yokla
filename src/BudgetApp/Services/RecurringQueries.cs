using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>An occurrence of a recurring definition that belongs to the current month but has not been generated yet.</summary>
public record PendingOccurrence(RecurringTransaction Definition, DateTime DueDate);

/// <summary>
/// Date rules of recurring definitions, shared by <see cref="RecurringTransactionService"/> (generation) and
/// <see cref="ForecastService"/> (what is still to come this month).
/// </summary>
internal static class RecurringQueries
{
    /// <summary>How far back a definition is caught up after a long absence (current month included).</summary>
    public const int MaxCatchUpMonths = 12;

    /// <summary>The month a date belongs to, as its first day.</summary>
    public static DateTime Period(DateTime date) => new(date.Year, date.Month, 1);

    /// <summary>The day a definition falls on in a given month: day 31 becomes the last day of shorter months.</summary>
    public static DateTime DueDate(int dayOfMonth, DateTime period) =>
        new(period.Year, period.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(period.Year, period.Month)));

    /// <summary>Whether an occurrence due on that day lies between the definition's start and end dates.</summary>
    public static bool IsInWindow(RecurringTransaction definition, DateTime due) =>
        due >= definition.StartDate.Date && (definition.EndDate is null || due <= definition.EndDate.Value.Date);

    /// <summary>The first month that has not been handled yet (never more than the catch-up limit behind).</summary>
    public static DateTime FirstUnprocessedPeriod(RecurringTransaction definition, DateTime currentPeriod)
    {
        var period = Period(definition.StartDate);
        if (definition.LastGeneratedPeriod is { } last && last.AddMonths(1) > period)
            period = last.AddMonths(1);

        var oldestAllowed = currentPeriod.AddMonths(-(MaxCatchUpMonths - 1));
        return period < oldestAllowed ? oldestAllowed : period;
    }

    /// <summary>
    /// Due date of the next occurrence that has not been handled yet, ignoring pause and end date.
    /// Months whose due day lies before the start date are skipped.
    /// </summary>
    public static DateTime NextUnprocessedDueDate(RecurringTransaction definition, DateTime today)
    {
        var period = FirstUnprocessedPeriod(definition, Period(today));
        var due = DueDate(definition.DayOfMonth, period);
        while (due < definition.StartDate.Date)
        {
            period = period.AddMonths(1);
            due = DueDate(definition.DayOfMonth, period);
        }

        return due;
    }

    /// <summary>
    /// This month's occurrences of the user's active definitions that have not been generated yet.
    /// They are certain to come, so the month-end forecast counts them as they are.
    /// </summary>
    public static async Task<List<PendingOccurrence>> GetPendingAsync(ApplicationDbContext db, string userId, DateTime today)
    {
        var current = Period(today);
        var definitions = await db.RecurringTransactions
            .AsNoTracking()
            .Include(r => r.Category)
            .Where(r => r.UserId == userId && r.IsActive && (r.LastGeneratedPeriod == null || r.LastGeneratedPeriod < current))
            .ToListAsync();

        return definitions
            .Select(d => new PendingOccurrence(d, DueDate(d.DayOfMonth, current)))
            .Where(p => IsInWindow(p.Definition, p.DueDate))
            .OrderBy(p => p.DueDate)
            .ThenBy(p => p.Definition.Id)
            .ToList();
    }
}
