using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public enum AllowanceStatus
{
    /// <summary>There is money left for the period and today's share is not used up.</summary>
    Ok,
    /// <summary>There is money left for the period, but today's spending is above today's share.</summary>
    OverToday,
    /// <summary>Nothing is left until the next income: nothing was available, or today's spending used it all up.</summary>
    Deficit
}

/// <summary>
/// "How much can I spend today?" The money on hand, minus what recurring expenses will still take before the next
/// income, divided by the days until that income. Everything is measured at the start of the day: what is spent
/// during the day does not shrink <see cref="DailyAmount"/>, it only uses it up; the rest carries over to tomorrow.
/// </summary>
/// <param name="PeriodEnd">The day the next income arrives; without recurring income, the first day of next month.</param>
/// <param name="NextIncomeDate">Null when the user has no recurring income to wait for.</param>
/// <param name="DaysLeft">Days the money has to last, today included, the income day excluded.</param>
/// <param name="Available">Money on hand at the start of today minus <paramref name="UpcomingExpenses"/>; may be negative.</param>
/// <param name="UpcomingExpenses">Recurring expenses that will be generated before <paramref name="PeriodEnd"/>.</param>
/// <param name="DailyAmount">Today's share, rounded down to the kuruş; zero when nothing is available.</param>
/// <param name="SpentToday">Today's variable expenses.</param>
/// <param name="TomorrowAmount">Tomorrow's share if nothing else is spent today; null when the income arrives tomorrow.</param>
public record DailyAllowance(
    DateTime PeriodEnd,
    DateTime? NextIncomeDate,
    string? NextIncomeName,
    int DaysLeft,
    decimal Available,
    decimal UpcomingExpenses,
    decimal DailyAmount,
    decimal SpentToday,
    decimal? TomorrowAmount)
{
    public bool HasRecurringIncome => NextIncomeDate is not null;

    /// <summary>What is left of today's share; negative when today's spending went over it.</summary>
    public decimal RemainingToday => DailyAmount - SpentToday;

    /// <summary>What is left until the next income right now, today's spending included; negative when overspent.</summary>
    public decimal LeftForPeriod => Available - SpentToday;

    /// <summary>How far the user is below zero until the next income; zero when there is no shortfall.</summary>
    public decimal Shortfall => Math.Max(0m, -LeftForPeriod);

    /// <summary>
    /// A deficit does not wait for tomorrow: once today's spending has used up everything that was available, the
    /// status turns at once. Spending exactly the last share on the last day is the plan working, not a deficit.
    /// </summary>
    public AllowanceStatus Status =>
        Available <= 0 || LeftForPeriod < 0 || (LeftForPeriod == 0 && DaysLeft > 1) ? AllowanceStatus.Deficit
        : RemainingToday < 0 ? AllowanceStatus.OverToday
        : AllowanceStatus.Ok;
}

/// <summary>
/// "At this pace the money runs out before the next income": this month's daily pace measured against the same
/// money the daily allowance is cut from.
/// </summary>
/// <param name="DailyPace">This month's variable spending per day so far.</param>
/// <param name="DailyAmount">Today's allowance, for comparison.</param>
/// <param name="RunsOutOn">The day the money available now is used up at that pace.</param>
public record PaceWarning(decimal DailyPace, decimal DailyAmount, DateTime RunsOutOn);

public static class SpendingPace
{
    /// <summary>The first days of a month say little about a pace (one big purchase would dominate), as in warning rule 1.</summary>
    public const int MinDaysElapsed = 3;

    /// <summary>
    /// When the money available now (<see cref="DailyAllowance.Available"/>) would be used up at this month's daily
    /// pace: today + available ÷ pace, counting only whole days. Null when that is not before the next income
    /// (nothing to warn about), when there is nothing available (the allowance already shows the deficit), when
    /// nothing was spent, or too early in the month.
    /// </summary>
    public static PaceWarning? Check(DailyAllowance allowance, decimal dailyPace, int daysElapsed, DateTime today)
    {
        if (daysElapsed < MinDaysElapsed || dailyPace <= 0 || allowance.Available <= 0)
            return null;

        var days = (int)Math.Floor(allowance.Available / dailyPace);
        var runsOutOn = today.Date.AddDays(days);
        return runsOutOn < allowance.PeriodEnd.Date
            ? new PaceWarning(dailyPace, allowance.DailyAmount, runsOutOn)
            : null;
    }
}

public interface IDailyAllowanceService
{
    /// <summary>Null when the user has no transactions yet (there is nothing to base an allowance on).</summary>
    Task<DailyAllowance?> GetAsync(string userId, DateTime today);
}

public class DailyAllowanceService(ApplicationDbContext db) : IDailyAllowanceService
{
    /// <summary>How far ahead the next recurring income is looked for.</summary>
    private const int IncomeSearchMonths = 13;

    public async Task<DailyAllowance?> GetAsync(string userId, DateTime today)
    {
        today = today.Date;
        var tomorrow = today.AddDays(1);

        // The whole history up to today: the balance is carried across months, it is not reset on the 1st.
        var rows = await MonthData.GetRowsAsync(db, userId, DateTime.MinValue, tomorrow);
        if (rows.Count == 0)
            return null;

        var income = MonthData.Income(rows);
        var spentBeforeToday = rows.Where(r => r.IsExpense && r.Date.Date < today).Sum(r => r.Amount);
        var todaysExpenses = rows.Where(r => r.IsExpense && r.Date.Date == today).ToList();
        // Rent, bills and recurring occurrences that land today are not "today's spending": they come off the pool.
        var fixedToday = todaysExpenses.Where(r => r.IsFixedLike).Sum(r => r.Amount);
        var spentToday = todaysExpenses.Where(r => !r.IsFixedLike).Sum(r => r.Amount);
        var setAside = await GoalContributionsAsync(userId, tomorrow);
        var onHand = income - spentBeforeToday - fixedToday - setAside;

        var definitions = await RecurringQueries.GetActiveAsync(db, userId);
        var nextIncome = definitions
            .Where(d => d.Type == TransactionType.Income)
            .Select(d => new
            {
                Definition = d,
                Due = RecurringQueries.UpcomingDueDates(d, today, today.AddMonths(IncomeSearchMonths))
                    .Where(due => due > today)
                    .Select(due => (DateTime?)due)
                    .FirstOrDefault()
            })
            .Where(x => x.Due is not null)
            .OrderBy(x => x.Due)
            .ThenBy(x => x.Definition.Id)
            .FirstOrDefault();

        // Without recurring income the money has to last until the end of the month.
        var periodEnd = nextIncome?.Due ?? RecurringQueries.Period(today).AddMonths(1);
        var daysLeft = (periodEnd - today).Days;

        // Expenses due on the income day itself are paid from that income, so the range is exclusive.
        var upcoming = definitions
            .Where(d => d.Type == TransactionType.Expense)
            .Sum(d => RecurringQueries.UpcomingDueDates(d, today, periodEnd).Count() * d.Amount);

        var available = onHand - upcoming;
        var daily = available > 0 ? RoundDown(available / daysLeft) : 0m;
        decimal? tomorrowAmount = daysLeft > 1
            ? RoundDown(Math.Max(0m, available - spentToday) / (daysLeft - 1))
            : null;

        var name = nextIncome is null ? null
            : string.IsNullOrWhiteSpace(nextIncome.Definition.ItemName) ? nextIncome.Definition.Category?.Name
            : nextIncome.Definition.ItemName;

        return new DailyAllowance(periodEnd, nextIncome?.Due, name, daysLeft, available, upcoming, daily, spentToday, tomorrowAmount);
    }

    /// <summary>Everything the user has put into goals so far (by hand or through a month close): that money is set aside.</summary>
    private async Task<decimal> GoalContributionsAsync(string userId, DateTime beforeExclusive)
    {
        // Summed in memory: SQLite cannot aggregate decimal columns reliably.
        var amounts = await db.GoalContributions
            .AsNoTracking()
            .Where(c => c.Goal!.UserId == userId && c.Date < beforeExclusive)
            .Select(c => c.Amount)
            .ToListAsync();
        return amounts.Sum();
    }

    /// <summary>Down to the kuruş, so the daily shares never add up to more than is available.</summary>
    private static decimal RoundDown(decimal value) => Math.Floor(value * 100m) / 100m;
}
