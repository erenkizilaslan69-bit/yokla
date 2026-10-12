using BudgetApp.Services;

namespace BudgetApp.Models;

public class ReportCardViewModel
{
    /// <summary>The card of the selected month (without a grade it carries the reason).</summary>
    public required ReportCard Card { get; init; }

    /// <summary>The finished months, newest first, for moving between cards.</summary>
    public List<ReportCardSummary> History { get; init; } = [];

    public DateTime Month => new(Card.Year, Card.Month, 1);
}
