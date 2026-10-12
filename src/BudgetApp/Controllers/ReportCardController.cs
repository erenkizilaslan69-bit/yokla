using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public class ReportCardController(IReportCardService reportCards) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index(int? year, int? month)
    {
        var today = DateTime.Today;
        // Without a (valid) month the last finished one is shown: that is the newest card there can be.
        var selected = year is >= 2000 and <= 2100 && month is >= 1 and <= 12
            ? new DateTime(year.Value, month.Value, 1)
            : new DateTime(today.Year, today.Month, 1).AddMonths(-1);

        return View(new ReportCardViewModel
        {
            Card = await reportCards.GetAsync(UserId, selected.Year, selected.Month, today),
            History = await reportCards.GetHistoryAsync(UserId, today)
        });
    }
}
