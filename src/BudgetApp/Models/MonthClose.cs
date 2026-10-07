namespace BudgetApp.Models;

public class MonthClose
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int Year { get; set; }

    public int Month { get; set; }

    /// <summary>Income minus expenses of that month; may be negative.</summary>
    public decimal Remaining { get; set; }

    /// <summary>Set when the remainder was transferred to a goal.</summary>
    public int? TransferredGoalId { get; set; }
}
