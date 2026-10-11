using BudgetApp.Services;

namespace BudgetApp.Models;

public class AnalysisViewModel
{
    public required MonthlyAnalysis Analysis { get; init; }
    public required MonthPickerViewModel Picker { get; init; }

    /// <summary>The user's category limits with the spending of the shown month.</summary>
    public List<BudgetProgress> Budgets { get; init; } = [];
}
