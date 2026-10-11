using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public enum RecurringStatus
{
    Active,
    /// <summary>Stopped by the user; nothing is generated until it is resumed.</summary>
    Paused,
    /// <summary>The end date has passed; there is no occurrence left to generate.</summary>
    Ended
}

/// <summary>A definition as shown in the list: <see cref="NextDueDate"/> is set only while it is active.</summary>
public record RecurringListItem(RecurringTransaction Definition, DateTime? NextDueDate, RecurringStatus Status);

/// <summary>What the running definitions add up to per month.</summary>
public record RecurringSummary(decimal MonthlyIncome, decimal MonthlyExpense)
{
    public decimal Net => MonthlyIncome - MonthlyExpense;
}

public interface IRecurringTransactionService
{
    Task<List<RecurringListItem>> GetAllAsync(string userId, DateTime today);
    Task<RecurringTransaction?> GetAsync(string userId, int id);
    Task<RecurringTransaction> CreateAsync(string userId, RecurringTransaction input, DateTime today);
    Task<bool> UpdateAsync(string userId, RecurringTransaction input, DateTime today);
    Task<bool> PauseAsync(string userId, int id);
    Task<bool> ResumeAsync(string userId, int id, DateTime today);
    Task<bool> DeleteAsync(string userId, int id);

    /// <summary>
    /// Makes an existing transaction the first occurrence of a new monthly definition ("repeat every month").
    /// Null when the transaction does not exist or belongs to someone else.
    /// </summary>
    Task<RecurringTransaction?> CreateFromTransactionAsync(string userId, int transactionId, DateTime today);

    /// <summary>
    /// Creates the transactions of all occurrences that are due and not handled yet. Safe to call on every request:
    /// a month is generated at most once per definition. Returns what was created.
    /// </summary>
    Task<List<Transaction>> GenerateDueAsync(string userId, DateTime today);

    /// <summary>This month's occurrences that have not been generated yet, ordered by due date.</summary>
    Task<List<PendingOccurrence>> GetPendingAsync(string userId, DateTime today);

    Task<RecurringSummary> GetSummaryAsync(string userId, DateTime today);
}

public class RecurringTransactionService(ApplicationDbContext db) : IRecurringTransactionService
{
    public async Task<List<RecurringListItem>> GetAllAsync(string userId, DateTime today)
    {
        var definitions = await db.RecurringTransactions
            .AsNoTracking()
            .Include(r => r.Category)
            .Where(r => r.UserId == userId)
            .ToListAsync();

        return definitions
            .Select(d => ToListItem(d, today))
            .OrderBy(i => i.Status)
            .ThenBy(i => i.NextDueDate ?? DateTime.MaxValue)
            .ThenBy(i => i.Definition.Id)
            .ToList();
    }

    public Task<RecurringTransaction?> GetAsync(string userId, int id) =>
        db.RecurringTransactions
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);

    public async Task<RecurringTransaction> CreateAsync(string userId, RecurringTransaction input, DateTime today)
    {
        var amount = Round(input.Amount);
        await ValidateAsync(input.Type, amount, input.CategoryId, input.DayOfMonth);

        // No bulk generation into earlier months (they may already have been closed and carried over).
        var start = input.StartDate.Date;
        if (start < RecurringQueries.Period(today))
            throw new ArgumentException("Start date cannot be before the current month.", nameof(input));
        ValidateEndDate(start, input.EndDate);

        var entity = new RecurringTransaction
        {
            UserId = userId,
            Type = input.Type,
            Amount = amount,
            CategoryId = input.CategoryId,
            ItemName = Clean(input.ItemName),
            Note = Clean(input.Note),
            DayOfMonth = input.DayOfMonth,
            StartDate = start,
            EndDate = input.EndDate?.Date,
            IsActive = true,
            CreatedAt = today.Date
        };
        db.RecurringTransactions.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> UpdateAsync(string userId, RecurringTransaction input, DateTime today)
    {
        var entity = await db.RecurringTransactions.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == input.Id);
        if (entity is null)
            return false;

        var amount = Round(input.Amount);
        await ValidateAsync(input.Type, amount, input.CategoryId, input.DayOfMonth);
        ValidateEndDate(entity.StartDate, input.EndDate);

        // Extending or removing an end date that has already passed works like "resume": the gap is not backfilled.
        var hadEnded = entity.EndDate is { } oldEnd && oldEnd.Date < today.Date;
        var runsAgain = input.EndDate is null || input.EndDate.Value.Date >= today.Date;
        if (hadEnded && runsAgain && entity.StartDate < today.Date)
            entity.StartDate = today.Date;

        // Already generated transactions are history and stay as they are; only future occurrences change.
        entity.Type = input.Type;
        entity.Amount = amount;
        entity.CategoryId = input.CategoryId;
        entity.ItemName = Clean(input.ItemName);
        entity.Note = Clean(input.Note);
        entity.DayOfMonth = input.DayOfMonth;
        entity.EndDate = input.EndDate?.Date;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> PauseAsync(string userId, int id)
    {
        var entity = await db.RecurringTransactions.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (entity is null)
            return false;

        entity.IsActive = false;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResumeAsync(string userId, int id, DateTime today)
    {
        var entity = await db.RecurringTransactions.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (entity is null)
            return false;

        entity.IsActive = true;
        // The months it was paused for are not filled in afterwards: it starts again from today.
        if (entity.StartDate < today.Date)
            entity.StartDate = today.Date;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(string userId, int id)
    {
        var entity = await db.RecurringTransactions.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (entity is null)
            return false;

        // Generated transactions stay: the foreign key is set to null and they become ordinary transactions.
        db.RecurringTransactions.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<RecurringTransaction?> CreateFromTransactionAsync(string userId, int transactionId, DateTime today)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.UserId == userId && t.Id == transactionId);
        if (transaction is null)
            return null;
        if (transaction.RecurringTransactionId is not null)
            throw new InvalidOperationException("The transaction already belongs to a recurring definition.");

        var period = RecurringQueries.Period(transaction.Date);
        var currentPeriod = RecurringQueries.Period(today);
        var definition = new RecurringTransaction
        {
            UserId = userId,
            Type = transaction.Type,
            Amount = transaction.Amount,
            CategoryId = transaction.CategoryId,
            ItemName = transaction.ItemName,
            Note = transaction.Note,
            DayOfMonth = transaction.Date.Day,
            // An older transaction does not make the months in between appear: generation starts with this month.
            StartDate = transaction.Date.Date < currentPeriod ? currentPeriod : transaction.Date.Date,
            IsActive = true,
            LastGeneratedPeriod = period, // the transaction itself is that month's occurrence
            CreatedAt = today.Date
        };

        transaction.RecurringTransaction = definition;
        transaction.RecurringPeriod = period;
        db.RecurringTransactions.Add(definition);
        await db.SaveChangesAsync();
        return definition;
    }

    public async Task<List<Transaction>> GenerateDueAsync(string userId, DateTime today)
    {
        var currentPeriod = RecurringQueries.Period(today);
        var definitions = await db.RecurringTransactions
            .Include(r => r.Category)
            .Where(r => r.UserId == userId && r.IsActive && (r.LastGeneratedPeriod == null || r.LastGeneratedPeriod < currentPeriod))
            .ToListAsync();

        var created = new List<Transaction>();
        foreach (var definition in definitions)
        {
            var batch = new List<Transaction>();
            var handledBefore = definition.LastGeneratedPeriod;

            for (var period = RecurringQueries.FirstUnprocessedPeriod(definition, currentPeriod);
                 period <= currentPeriod;
                 period = period.AddMonths(1))
            {
                var due = RecurringQueries.DueDate(definition.DayOfMonth, period);
                if (due > today.Date)
                    break; // its day has not come yet
                if (definition.EndDate is { } end && due > end.Date)
                    break; // the definition is over

                // A month whose due day lies before the start date is settled without a transaction.
                if (due >= definition.StartDate.Date)
                    batch.Add(NewOccurrence(definition, period, due));
                definition.LastGeneratedPeriod = period;
            }

            if (definition.LastGeneratedPeriod == handledBefore)
                continue;

            // One SaveChanges per definition: the transactions and the "handled" marker are written together.
            db.Transactions.AddRange(batch);
            try
            {
                await db.SaveChangesAsync();
                created.AddRange(batch);
            }
            catch (DbUpdateException)
            {
                // Another request generated the same month first (unique index on definition + month). Nothing to do.
                foreach (var transaction in batch)
                    db.Entry(transaction).State = EntityState.Detached;
                await db.Entry(definition).ReloadAsync();
            }
        }

        return created;
    }

    public Task<List<PendingOccurrence>> GetPendingAsync(string userId, DateTime today) =>
        RecurringQueries.GetPendingAsync(db, userId, today);

    public async Task<RecurringSummary> GetSummaryAsync(string userId, DateTime today)
    {
        var running = (await GetAllAsync(userId, today))
            .Where(i => i.Status == RecurringStatus.Active)
            .Select(i => i.Definition)
            .ToList();

        return new RecurringSummary(
            running.Where(d => d.Type == TransactionType.Income).Sum(d => d.Amount),
            running.Where(d => d.Type == TransactionType.Expense).Sum(d => d.Amount));
    }

    private static RecurringListItem ToListItem(RecurringTransaction definition, DateTime today)
    {
        var next = RecurringQueries.NextUnprocessedDueDate(definition, today);
        var status = definition.EndDate is { } end && next > end.Date ? RecurringStatus.Ended
            : !definition.IsActive ? RecurringStatus.Paused
            : RecurringStatus.Active;
        return new RecurringListItem(definition, status == RecurringStatus.Active ? next : null, status);
    }

    private static Transaction NewOccurrence(RecurringTransaction definition, DateTime period, DateTime due) => new()
    {
        UserId = definition.UserId,
        Type = definition.Type,
        Amount = definition.Amount,
        Date = due,
        CategoryId = definition.CategoryId,
        Category = definition.Category,
        ItemName = definition.ItemName,
        Note = definition.Note,
        RecurringTransactionId = definition.Id,
        RecurringPeriod = period
    };

    private async Task ValidateAsync(TransactionType type, decimal amount, int categoryId, int dayOfMonth)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");
        if (dayOfMonth is < 1 or > 31)
            throw new ArgumentException("Day of month must be between 1 and 31.");

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category is null)
            throw new ArgumentException("Category does not exist.");
        if (category.Type != type)
            throw new ArgumentException("Category type does not match the transaction type.");
    }

    private static void ValidateEndDate(DateTime start, DateTime? end)
    {
        if (end is { } value && value.Date < start.Date)
            throw new ArgumentException("End date cannot be before the start date.");
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
