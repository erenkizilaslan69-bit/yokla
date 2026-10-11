using BudgetApp.Services;

namespace BudgetApp.Models;

public class RecurringIndexViewModel
{
    public required List<RecurringListItem> Items { get; init; }
    public required RecurringSummary Summary { get; init; }
}
