using System.Globalization;
using BudgetApp.Services;

namespace BudgetApp.Models;

public class HomeViewModel
{
    public GoalProgress? ActiveGoal { get; init; }

    public PendingMonthClose? Pending { get; init; }

    /// <summary>A goal reached within the last 30 days while no goal is open; the home page celebrates it.</summary>
    public Goal? Celebration { get; init; }

    /// <summary>Month-end balance projection; null when there are no transactions this month.</summary>
    public BalanceProjection? Projection { get; init; }

    /// <summary>Goal ETA; null when it cannot be calculated (no goal or no history).</summary>
    public GoalForecast? GoalForecast { get; init; }

    /// <summary>Income, expenses and balance of the current month.</summary>
    public TransactionTotals? Totals { get; init; }

    /// <summary>The user's newest transactions across all months.</summary>
    public List<Transaction> Recent { get; init; } = [];

    /// <summary>This month's recurring occurrences that have not been generated yet (already part of the projection).</summary>
    public List<PendingOccurrence> Upcoming { get; init; } = [];

    /// <summary>
    /// Amount that would be moved to the goal: the part of the surplus not already added by hand,
    /// capped at what the goal still needs.
    /// </summary>
    public decimal? TransferAmount =>
        Pending?.TargetGoal is not null && ActiveGoal is not null
            ? Math.Min(Pending.Transferable, ActiveGoal.Missing)
            : null;

    public string? PendingMonthName =>
        Pending is null ? null : CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(Pending.Month);
}
