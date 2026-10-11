using BudgetApp.Data;
using BudgetApp.Models;

namespace BudgetApp.Tests;

/// <summary>Seeded category ids and small helpers for arranging test data.</summary>
internal static class TestData
{
    public const int Income = 1;         // KYK Bursu
    public const int Market = 5;         // variable expense
    public const int Rent = 6;           // Kira/Yurt, fixed
    public const int Bills = 7;          // Faturalar, fixed
    public const int Transport = 8;      // Ulaşım, variable
    public const int EatingOut = 14;     // Dışarıda Yemek, unnecessary
    public const int Coffee = 15;        // Kafe/Kahve, unnecessary
    public const int Entertainment = 16; // Eğlence, unnecessary

    public static Transaction Add(ApplicationDbContext db, string userId, TransactionType type, decimal amount,
        DateTime date, int categoryId, string? itemName = null)
    {
        var tx = new Transaction
        {
            UserId = userId, Type = type, Amount = amount, Date = date, CategoryId = categoryId, ItemName = itemName
        };
        db.Transactions.Add(tx);
        db.SaveChanges();
        return tx;
    }

    public static Transaction AddIncome(ApplicationDbContext db, string userId, decimal amount, DateTime date) =>
        Add(db, userId, TransactionType.Income, amount, date, Income);

    public static Transaction AddExpense(ApplicationDbContext db, string userId, decimal amount, DateTime date,
        int categoryId = Market, string? itemName = null) =>
        Add(db, userId, TransactionType.Expense, amount, date, categoryId, itemName);

    /// <summary>A recurring definition inserted directly (no validation), e.g. with a start date in the past.</summary>
    public static RecurringTransaction AddRecurring(ApplicationDbContext db, string userId, TransactionType type, decimal amount,
        int day, DateTime start, int? categoryId = null, string? itemName = null, bool isActive = true,
        DateTime? lastGenerated = null, DateTime? end = null)
    {
        var definition = new RecurringTransaction
        {
            UserId = userId, Type = type, Amount = amount, DayOfMonth = day, StartDate = start, EndDate = end,
            CategoryId = categoryId ?? (type == TransactionType.Income ? Income : Rent),
            ItemName = itemName, IsActive = isActive, LastGeneratedPeriod = lastGenerated, CreatedAt = start
        };
        db.RecurringTransactions.Add(definition);
        db.SaveChanges();
        return definition;
    }

    /// <summary>An expense that was generated from a recurring definition (marked with its month).</summary>
    public static Transaction AddGeneratedExpense(ApplicationDbContext db, string userId, decimal amount, DateTime date,
        int categoryId, string? itemName = null)
    {
        var tx = new Transaction
        {
            UserId = userId, Type = TransactionType.Expense, Amount = amount, Date = date, CategoryId = categoryId,
            ItemName = itemName, RecurringPeriod = new DateTime(date.Year, date.Month, 1)
        };
        db.Transactions.Add(tx);
        db.SaveChanges();
        return tx;
    }

    public static Goal AddGoal(ApplicationDbContext db, string userId, decimal target, string name = "Kulaklık",
        decimal saved = 0m)
    {
        var goal = new Goal { UserId = userId, Name = name, TargetAmount = target, CreatedAt = new DateTime(2026, 1, 1) };
        db.Goals.Add(goal);
        db.SaveChanges();
        if (saved > 0)
        {
            db.GoalContributions.Add(new GoalContribution
            {
                GoalId = goal.Id, Amount = saved, Date = new DateTime(2026, 1, 2), Source = GoalContributionSource.Manual
            });
            db.SaveChanges();
        }
        return goal;
    }
}
