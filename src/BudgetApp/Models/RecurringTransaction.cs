namespace BudgetApp.Models;

/// <summary>
/// A monthly recurring income or expense (rent, scholarship, subscription). Each month one <see cref="Transaction"/>
/// is generated from it on its due day.
/// </summary>
public class RecurringTransaction
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public TransactionType Type { get; set; }

    /// <summary>Always positive; direction is determined by <see cref="Type"/>.</summary>
    public decimal Amount { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    public string? ItemName { get; set; }

    public string? Note { get; set; }

    /// <summary>1–31. In shorter months the occurrence falls on the last day of the month.</summary>
    public int DayOfMonth { get; set; }

    /// <summary>Occurrences due before this date are never generated. Moved to "today" when a paused definition resumes.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Occurrences due after this date are never generated; null means open-ended.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>False while the user has paused the definition.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// First day of the last month that has been handled. That month and everything before it is never generated
    /// again, which is why deleting a generated transaction does not bring it back.
    /// </summary>
    public DateTime? LastGeneratedPeriod { get; set; }

    public DateTime CreatedAt { get; set; }
}
