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
}
