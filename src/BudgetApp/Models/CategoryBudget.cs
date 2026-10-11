namespace BudgetApp.Models;

/// <summary>How far into a monthly category limit the spending of the month has come.</summary>
public enum BudgetLevel
{
    /// <summary>At or below 80% of the limit.</summary>
    None = 0,
    /// <summary>More than 80% and at most 100% of the limit.</summary>
    Approaching = 1,
    /// <summary>More than 100% of the limit.</summary>
    Exceeded = 2
}

/// <summary>A monthly spending limit the user set for one expense category.</summary>
public class CategoryBudget
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    public decimal MonthlyLimit { get; set; }

    /// <summary>First day of the month the last budget warning was given for.</summary>
    public DateTime? WarnedPeriod { get; set; }

    /// <summary>
    /// The highest warning already given in <see cref="WarnedPeriod"/>; a warning is only repeated in the same
    /// month when the level goes up (approaching, then exceeded). Reset when the limit is changed.
    /// </summary>
    public BudgetLevel WarnedLevel { get; set; }
}
