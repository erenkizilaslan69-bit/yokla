using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public record GoalProgress(Goal Goal, decimal Saved)
{
    /// <summary>Amount still missing to reach the target (never negative).</summary>
    public decimal Missing => Math.Max(0m, Goal.TargetAmount - Saved);

    /// <summary>Fill percentage clamped to 0–100.</summary>
    public int Percent => Goal.TargetAmount <= 0
        ? 0
        : (int)Math.Clamp(Math.Round(Saved / Goal.TargetAmount * 100m, MidpointRounding.ToZero), 0m, 100m);
}

public interface IGoalService
{
    Task<List<GoalProgress>> GetAllAsync(string userId);
    Task<GoalProgress?> GetActiveAsync(string userId);
    Task<Goal> CreateAsync(string userId, string name, decimal targetAmount, DateTime now);
    Task<GoalContribution?> AddContributionAsync(string userId, int goalId, decimal amount, GoalContributionSource source, DateTime date);
}

public class GoalService(ApplicationDbContext db) : IGoalService
{
    public async Task<List<GoalProgress>> GetAllAsync(string userId)
    {
        var goals = await db.Goals
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.IsCompleted)
            .ThenBy(g => g.CreatedAt)
            .ThenBy(g => g.Id)
            .ToListAsync();

        var result = new List<GoalProgress>(goals.Count);
        foreach (var goal in goals)
            result.Add(new GoalProgress(goal, await GoalQueries.GetSavedAsync(db, goal.Id)));
        return result;
    }

    public async Task<GoalProgress?> GetActiveAsync(string userId)
    {
        var goal = await GoalQueries.GetActiveAsync(db, userId);
        return goal is null ? null : new GoalProgress(goal, await GoalQueries.GetSavedAsync(db, goal.Id));
    }

    public async Task<Goal> CreateAsync(string userId, string name, decimal targetAmount, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Goal name is required.", nameof(name));
        if (targetAmount <= 0)
            throw new ArgumentException("Target amount must be greater than zero.", nameof(targetAmount));

        var goal = new Goal
        {
            UserId = userId,
            Name = name.Trim(),
            TargetAmount = targetAmount,
            CreatedAt = now
        };
        db.Goals.Add(goal);
        await db.SaveChangesAsync();
        return goal;
    }

    public async Task<GoalContribution?> AddContributionAsync(
        string userId, int goalId, decimal amount, GoalContributionSource source, DateTime date)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId && g.Id == goalId);
        if (goal is null)
            return null;
        if (goal.IsCompleted)
            throw new InvalidOperationException("Goal is already completed.");

        var saved = await GoalQueries.GetSavedAsync(db, goal.Id);
        var contribution = new GoalContribution { GoalId = goal.Id, Amount = amount, Date = date.Date, Source = source };
        db.GoalContributions.Add(contribution);
        goal.IsCompleted = saved + amount >= goal.TargetAmount;
        await db.SaveChangesAsync();
        return contribution;
    }
}
