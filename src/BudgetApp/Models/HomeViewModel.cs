using System.Globalization;
using BudgetApp.Services;

namespace BudgetApp.Models;

public class HomeViewModel
{
    public GoalProgress? ActiveGoal { get; init; }

    public PendingMonthClose? Pending { get; init; }

    /// <summary>Amount that would be moved to the goal: the remainder, capped at what the goal still needs.</summary>
    public decimal? TransferAmount =>
        Pending?.TargetGoal is not null && ActiveGoal is not null
            ? Math.Min(Pending.Remaining, ActiveGoal.Missing)
            : null;

    public string? PendingMonthName =>
        Pending is null ? null : CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(Pending.Month);
}
