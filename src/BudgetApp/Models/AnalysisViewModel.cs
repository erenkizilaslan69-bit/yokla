using BudgetApp.Services;

namespace BudgetApp.Models;

public class AnalysisViewModel
{
    public required MonthlyAnalysis Analysis { get; init; }
    public required MonthPickerViewModel Picker { get; init; }
}
