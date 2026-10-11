using BudgetApp.Data;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>
/// Month-end projection:
/// <c>Projected = (Income + PendingIncome) - (FixedExpenses + PendingExpenses) - VariableExpenses * daysInMonth / dayOfMonth</c>.
/// <para>
/// <see cref="FixedExpenses"/> are recorded expenses taken as they are: fixed categories and anything generated from a
/// recurring definition. <see cref="PendingIncome"/> and <see cref="PendingExpenses"/> are this month's recurring
/// occurrences that have not been generated yet. Only <see cref="VariableExpenses"/> are extrapolated.
/// </para>
/// </summary>
public record BalanceProjection(
    decimal Income, decimal FixedExpenses, decimal VariableExpenses, decimal Projected,
    decimal PendingIncome = 0m, decimal PendingExpenses = 0m)
{
    /// <summary>Income recorded so far plus the recurring income still expected this month.</summary>
    public decimal KnownIncome => Income + PendingIncome;
}

public enum GoalForecastStatus
{
    Reachable,
    /// <summary>The average monthly carry-over is zero or negative, so the goal will not be reached at this pace.</summary>
    NeedsCutback
}

public record GoalForecast(Goal Goal, GoalForecastStatus Status, int Months, DateTime? ReachDate);

public interface IForecastService
{
    /// <summary>Null when the user has neither transactions nor upcoming recurring occurrences this month.</summary>
    Task<BalanceProjection?> GetProjectedBalanceAsync(string userId, DateTime today);

    /// <summary>
    /// Null when there is no active goal or no completed month to average.
    /// <paramref name="extraMonthlySaving"/> is added to the average monthly carry-over (what-if savings).
    /// </summary>
    Task<GoalForecast?> GetGoalForecastAsync(string userId, DateTime today, decimal extraMonthlySaving = 0m);
}

public class ForecastService(ApplicationDbContext db) : IForecastService
{
    private const int HistoryMonths = 3;

    public async Task<BalanceProjection?> GetProjectedBalanceAsync(string userId, DateTime today)
    {
        var rows = await MonthData.GetMonthAsync(db, userId, today.Year, today.Month);
        var pending = await RecurringQueries.GetPendingAsync(db, userId, today);
        if (rows.Count == 0 && pending.Count == 0)
            return null;

        var income = MonthData.Income(rows);
        var fixedExpenses = rows.Where(r => r.IsExpense && r.IsFixedLike).Sum(r => r.Amount);
        var variableExpenses = rows.Where(r => r.IsExpense && !r.IsFixedLike).Sum(r => r.Amount);

        // Recurring occurrences that are still to come this month are certain, so they count as they are.
        var pendingIncome = pending.Where(p => p.Definition.Type == TransactionType.Income).Sum(p => p.Definition.Amount);
        var pendingExpenses = pending.Where(p => p.Definition.Type == TransactionType.Expense).Sum(p => p.Definition.Amount);

        // Multiply before dividing so round figures stay exact in decimal arithmetic.
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var projected = income + pendingIncome - fixedExpenses - pendingExpenses - variableExpenses * daysInMonth / today.Day;
        return new BalanceProjection(income, fixedExpenses, variableExpenses, projected, pendingIncome, pendingExpenses);
    }

    public async Task<GoalForecast?> GetGoalForecastAsync(string userId, DateTime today, decimal extraMonthlySaving = 0m)
    {
        var goal = await GoalQueries.GetActiveAsync(db, userId);
        if (goal is null)
            return null;

        var missing = goal.TargetAmount - await GoalQueries.GetSavedAsync(db, goal.Id);
        if (missing <= 0)
            return null;

        var currentMonth = new DateTime(today.Year, today.Month, 1);
        var rows = await MonthData.GetRowsAsync(db, userId, currentMonth.AddMonths(-HistoryMonths), currentMonth);

        // Net carry-over per completed month; months without any transaction are not averaged as zero.
        var monthlyNet = rows
            .GroupBy(r => (r.Date.Year, r.Date.Month))
            .Select(g => MonthData.Income(g) - MonthData.Expense(g))
            .ToList();
        if (monthlyNet.Count == 0)
            return null;

        var average = monthlyNet.Sum() / monthlyNet.Count + extraMonthlySaving;
        if (average <= 0)
            return new GoalForecast(goal, GoalForecastStatus.NeedsCutback, 0, null);

        var months = (int)Math.Ceiling(missing / average);
        return new GoalForecast(goal, GoalForecastStatus.Reachable, months, currentMonth.AddMonths(months));
    }
}
