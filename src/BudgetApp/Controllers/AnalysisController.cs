using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public class AnalysisController(IAnalysisService analysis, IBudgetService budgets) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index(int? year, int? month)
    {
        var today = DateTime.Today;
        var (y, m) = MonthPickerViewModel.Resolve(year, month, today);

        return View(new AnalysisViewModel
        {
            Analysis = await analysis.GetMonthlyAnalysisAsync(UserId, y, m, today),
            Budgets = await budgets.GetProgressAsync(UserId, y, m),
            Picker = new MonthPickerViewModel
            {
                Controller = "Analysis", Year = y, Month = m, IsCurrentMonth = y == today.Year && m == today.Month
            }
        });
    }
}
