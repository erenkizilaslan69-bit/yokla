using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>A category limit together with what was spent in the category in one month.</summary>
public record BudgetProgress(CategoryBudget Budget, decimal Spent)
{
    public decimal Limit => Budget.MonthlyLimit;

    /// <summary>Spent ÷ limit in percent; can be above 100.</summary>
    public decimal Percent => Limit <= 0 ? 0m : Spent / Limit * 100m;

    public decimal Remaining => Math.Max(0m, Limit - Spent);

    public BudgetLevel Level => BudgetMath.LevelFor(Spent, Limit);
}

public interface IBudgetService
{
    /// <summary>The user's limits with the month's spending, most used first.</summary>
    Task<List<BudgetProgress>> GetProgressAsync(string userId, int year, int month);

    Task<CategoryBudget?> GetAsync(string userId, int id);

    /// <summary>
    /// Sets the limit of an expense category: creates it, or changes the existing one (one limit per category).
    /// Changing the limit starts the month's warnings over.
    /// </summary>
    Task<CategoryBudget> SetAsync(string userId, int categoryId, decimal monthlyLimit);

    Task<bool> DeleteAsync(string userId, int id);
}

internal static class BudgetMath
{
    public const decimal ApproachingShare = 0.80m;

    /// <summary>More than 100% of the limit is exceeded; more than 80% is approaching.</summary>
    public static BudgetLevel LevelFor(decimal spent, decimal limit)
    {
        if (limit <= 0)
            return BudgetLevel.None;
        if (spent > limit)
            return BudgetLevel.Exceeded;
        return spent > ApproachingShare * limit ? BudgetLevel.Approaching : BudgetLevel.None;
    }
}

public class BudgetService(ApplicationDbContext db) : IBudgetService
{
    public async Task<List<BudgetProgress>> GetProgressAsync(string userId, int year, int month)
    {
        var budgets = await db.CategoryBudgets
            .AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.UserId == userId)
            .ToListAsync();
        if (budgets.Count == 0)
            return [];

        var rows = await MonthData.GetMonthAsync(db, userId, year, month);
        var spentByCategory = rows
            .Where(r => r.IsExpense)
            .GroupBy(r => r.CategoryId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        return budgets
            .Select(b => new BudgetProgress(b, spentByCategory.GetValueOrDefault(b.CategoryId)))
            .OrderByDescending(p => p.Percent)
            .ThenBy(p => p.Budget.Category!.Name)
            .ToList();
    }

    public Task<CategoryBudget?> GetAsync(string userId, int id) =>
        db.CategoryBudgets
            .AsNoTracking()
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id);

    public async Task<CategoryBudget> SetAsync(string userId, int categoryId, decimal monthlyLimit)
    {
        var limit = MoneyMath.Round(monthlyLimit);
        if (limit <= 0)
            throw new ArgumentException("Limit must be greater than zero.", nameof(monthlyLimit));

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category is null)
            throw new ArgumentException("Category does not exist.", nameof(categoryId));
        if (category.Type != TransactionType.Expense)
            throw new ArgumentException("Only expense categories can have a limit.", nameof(categoryId));

        var budget = await db.CategoryBudgets.FirstOrDefaultAsync(b => b.UserId == userId && b.CategoryId == categoryId);
        if (budget is null)
        {
            budget = new CategoryBudget { UserId = userId, CategoryId = categoryId, MonthlyLimit = limit };
            db.CategoryBudgets.Add(budget);
        }
        else if (budget.MonthlyLimit != limit)
        {
            // A new limit starts over: crossing 80% or 100% of it is worth a warning again.
            budget.MonthlyLimit = limit;
            budget.WarnedPeriod = null;
            budget.WarnedLevel = BudgetLevel.None;
        }

        await db.SaveChangesAsync();
        return budget;
    }

    public async Task<bool> DeleteAsync(string userId, int id)
    {
        var budget = await db.CategoryBudgets.FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id);
        if (budget is null)
            return false;

        db.CategoryBudgets.Remove(budget);
        await db.SaveChangesAsync();
        return true;
    }
}
