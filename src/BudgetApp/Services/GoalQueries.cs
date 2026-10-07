using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>Goal lookups shared by <see cref="GoalService"/> and <see cref="MonthCloseService"/>.</summary>
internal static class GoalQueries
{
    /// <summary>The oldest goal of the user that is not completed yet (the transfer target).</summary>
    public static Task<Goal?> GetActiveAsync(ApplicationDbContext db, string userId) =>
        db.Goals
            .Where(g => g.UserId == userId && !g.IsCompleted)
            .OrderBy(g => g.CreatedAt)
            .ThenBy(g => g.Id)
            .FirstOrDefaultAsync();

    // Summed in memory: SQLite cannot aggregate decimal columns reliably.
    public static async Task<decimal> GetSavedAsync(ApplicationDbContext db, int goalId) =>
        (await db.GoalContributions
            .AsNoTracking()
            .Where(c => c.GoalId == goalId)
            .Select(c => c.Amount)
            .ToListAsync()).Sum();
}
