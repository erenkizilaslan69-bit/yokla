namespace BudgetApp.Models;

public class Goal
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal TargetAmount { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>True once the contributions total reaches <see cref="TargetAmount"/>.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>The day the goal was reached (the date of the contribution that completed it); null while it is open.</summary>
    public DateTime? CompletedAt { get; set; }

    public List<GoalContribution> Contributions { get; set; } = [];
}
