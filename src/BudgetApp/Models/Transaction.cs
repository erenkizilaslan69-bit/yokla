namespace BudgetApp.Models;

public class Transaction
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Always positive; direction is determined by <see cref="Type"/>.</summary>
    public decimal Amount { get; set; }

    public TransactionType Type { get; set; }

    public DateTime Date { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    public string? ItemName { get; set; }

    public string? Note { get; set; }

    /// <summary>The recurring definition this transaction was generated from; null for manual entries and after the definition is deleted.</summary>
    public int? RecurringTransactionId { get; set; }

    public RecurringTransaction? RecurringTransaction { get; set; }

    /// <summary>
    /// First day of the month whose occurrence this transaction is. Kept separately from <see cref="Date"/> so the
    /// "one per definition per month" guarantee survives the user moving the transaction to another day or month.
    /// </summary>
    public DateTime? RecurringPeriod { get; set; }
}
