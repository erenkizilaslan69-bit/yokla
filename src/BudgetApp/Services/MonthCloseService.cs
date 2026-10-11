using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>
/// Data for the month-close card.
/// <see cref="Remaining"/> is the month's real income minus expenses. <see cref="AlreadyAdded"/> is what the user
/// manually put into goals during that month, and <see cref="Transferable"/> is <c>max(0, Remaining - AlreadyAdded)</c>:
/// the part that can still be transferred without counting the same money twice.
/// <see cref="TargetGoal"/> is set only when a transfer can actually be offered (Transferable &gt; 0 and an active goal).
/// </summary>
public record PendingMonthClose(
    int Year, int Month, decimal Remaining, Goal? TargetGoal, decimal AlreadyAdded, decimal Transferable);

public interface IMonthCloseService
{
    Task<decimal> CalculateRemainingAsync(string userId, int year, int month);
    Task<PendingMonthClose?> GetPendingAsync(string userId, DateTime today);
    Task<MonthClose> CloseAsync(string userId, int year, int month, bool transfer, DateTime today);
}

public class MonthCloseService(ApplicationDbContext db) : IMonthCloseService
{
    public async Task<decimal> CalculateRemainingAsync(string userId, int year, int month)
    {
        var rows = await MonthRowsAsync(userId, year, month);
        return Sum(rows);
    }

    public async Task<PendingMonthClose?> GetPendingAsync(string userId, DateTime today)
    {
        var previous = new DateTime(today.Year, today.Month, 1).AddMonths(-1);

        if (await db.MonthCloses.AnyAsync(m => m.UserId == userId && m.Year == previous.Year && m.Month == previous.Month))
            return null;

        var rows = await MonthRowsAsync(userId, previous.Year, previous.Month);
        if (rows.Count == 0)
            return null;

        var remaining = Sum(rows);
        var alreadyAdded = await ManualContributionsAsync(userId, previous.Year, previous.Month);
        var transferable = Transferable(remaining, alreadyAdded);
        var goal = transferable > 0 ? await GoalQueries.GetActiveAsync(db, userId) : null;
        return new PendingMonthClose(previous.Year, previous.Month, remaining, goal, alreadyAdded, transferable);
    }

    public async Task<MonthClose> CloseAsync(string userId, int year, int month, bool transfer, DateTime today)
    {
        if (year < 1 || year > 9999 || month < 1 || month > 12)
            throw new ArgumentException("Invalid year or month.");
        if (new DateTime(year, month, 1) >= new DateTime(today.Year, today.Month, 1))
            throw new ArgumentException("Only past months can be closed.");

        if (await db.MonthCloses.AnyAsync(m => m.UserId == userId && m.Year == year && m.Month == month))
            throw new InvalidOperationException("This month has already been closed.");

        // MonthClose.Remaining always stores the month's real balance; only the transferable part is reduced.
        var remaining = await CalculateRemainingAsync(userId, year, month);
        var transferable = Transferable(remaining, await ManualContributionsAsync(userId, year, month));
        var close = new MonthClose { UserId = userId, Year = year, Month = month, Remaining = remaining };

        await using var dbTransaction = await db.Database.BeginTransactionAsync();

        if (transfer && transferable > 0)
        {
            var goal = await GoalQueries.GetActiveAsync(db, userId);
            if (goal is not null)
            {
                var saved = await GoalQueries.GetSavedAsync(db, goal.Id);
                // Never transfer more than the goal still needs.
                var amount = Math.Min(transferable, goal.TargetAmount - saved);
                if (amount > 0)
                {
                    db.GoalContributions.Add(new GoalContribution
                    {
                        GoalId = goal.Id,
                        Amount = amount,
                        Date = today.Date,
                        Source = GoalContributionSource.MonthClose
                    });
                    goal.IsCompleted = saved + amount >= goal.TargetAmount;
                    close.TransferredGoalId = goal.Id;
                }
            }
        }

        db.MonthCloses.Add(close);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A concurrent request closed the same month first (unique index on UserId, Year, Month).
            throw new InvalidOperationException("This month has already been closed.");
        }

        await dbTransaction.CommitAsync();
        return close;
    }

    private async Task<List<(TransactionType Type, decimal Amount)>> MonthRowsAsync(string userId, int year, int month)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < end)
            .Select(t => new { t.Type, t.Amount })
            .ToListAsync();
        return rows.Select(r => (r.Type, r.Amount)).ToList();
    }

    /// <summary>
    /// What the user manually added to any of their goals during that month. That money was already taken out of
    /// the month's surplus by hand, so the month-end transfer must not move it a second time.
    /// </summary>
    private async Task<decimal> ManualContributionsAsync(string userId, int year, int month)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var amounts = await db.GoalContributions
            .AsNoTracking()
            .Where(c => c.Source == GoalContributionSource.Manual && c.Date >= start && c.Date < end && c.Goal!.UserId == userId)
            .Select(c => c.Amount)
            .ToListAsync();
        return amounts.Sum();
    }

    private static decimal Transferable(decimal remaining, decimal alreadyAdded) =>
        Math.Max(0m, remaining - alreadyAdded);

    // Summed in memory: SQLite cannot aggregate decimal columns reliably.
    private static decimal Sum(List<(TransactionType Type, decimal Amount)> rows) =>
        rows.Sum(r => r.Type == TransactionType.Income ? r.Amount : -r.Amount);
}
