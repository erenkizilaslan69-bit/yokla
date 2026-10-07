using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public record TransactionTotals(decimal Income, decimal Expense)
{
    public decimal Balance => Income - Expense;
}

public interface ITransactionService
{
    Task<List<Transaction>> GetByMonthAsync(string userId, int year, int month);
    Task<Transaction?> GetAsync(string userId, int id);
    Task<Transaction> CreateAsync(string userId, Transaction input);
    Task<bool> UpdateAsync(string userId, Transaction input);
    Task<bool> DeleteAsync(string userId, int id);
    Task<TransactionTotals> GetTotalsAsync(string userId, int year, int month);
}

public class TransactionService(ApplicationDbContext db, TimeProvider? timeProvider = null) : ITransactionService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<List<Transaction>> GetByMonthAsync(string userId, int year, int month)
    {
        var (start, end) = MonthRange(year, month);
        return db.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < end)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToListAsync();
    }

    public Task<Transaction?> GetAsync(string userId, int id) =>
        db.Transactions
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Id == id);

    public async Task<Transaction> CreateAsync(string userId, Transaction input)
    {
        await ValidateAsync(input);

        var entity = new Transaction
        {
            UserId = userId,
            Amount = input.Amount,
            Type = input.Type,
            Date = input.Date.Date,
            CategoryId = input.CategoryId,
            ItemName = Clean(input.ItemName),
            Note = Clean(input.Note)
        };
        db.Transactions.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> UpdateAsync(string userId, Transaction input)
    {
        var entity = await db.Transactions.FirstOrDefaultAsync(t => t.UserId == userId && t.Id == input.Id);
        if (entity is null)
            return false;

        await ValidateAsync(input);

        entity.Amount = input.Amount;
        entity.Type = input.Type;
        entity.Date = input.Date.Date;
        entity.CategoryId = input.CategoryId;
        entity.ItemName = Clean(input.ItemName);
        entity.Note = Clean(input.Note);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(string userId, int id)
    {
        var entity = await db.Transactions.FirstOrDefaultAsync(t => t.UserId == userId && t.Id == id);
        if (entity is null)
            return false;

        db.Transactions.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<TransactionTotals> GetTotalsAsync(string userId, int year, int month)
    {
        var (start, end) = MonthRange(year, month);

        // Amounts are projected first and summed in memory: SQLite cannot aggregate decimal columns reliably.
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < end)
            .Select(t => new { t.Type, t.Amount })
            .ToListAsync();

        var income = rows.Where(r => r.Type == TransactionType.Income).Sum(r => r.Amount);
        var expense = rows.Where(r => r.Type == TransactionType.Expense).Sum(r => r.Amount);
        return new TransactionTotals(income, expense);
    }

    private async Task ValidateAsync(Transaction input)
    {
        if (input.Amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(input));

        if (input.Date.Date > _time.GetLocalNow().Date)
            throw new ArgumentException("Transaction date cannot be in the future.", nameof(input));

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CategoryId);
        if (category is null)
            throw new ArgumentException("Category does not exist.", nameof(input));
        if (category.Type != input.Type)
            throw new ArgumentException("Category type does not match the transaction type.", nameof(input));
    }

    private static (DateTime Start, DateTime End) MonthRange(int year, int month)
    {
        var start = new DateTime(year, month, 1);
        return (start, start.AddMonths(1));
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
