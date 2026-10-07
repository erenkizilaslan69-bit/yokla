namespace BudgetApp.Models;

public class GoalContribution
{
    public int Id { get; set; }

    public int GoalId { get; set; }

    public Goal? Goal { get; set; }

    public decimal Amount { get; set; }

    public DateTime Date { get; set; }

    public GoalContributionSource Source { get; set; }
}
