using System.Diagnostics;
using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

public class HomeController(
    IGoalService goals, IMonthCloseService monthCloses, IForecastService forecast, ITransactionService transactions,
    IRecurringTransactionService recurring, IDailyAllowanceService allowance)
    : Controller
{
    private const int RecentCount = 5;

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return View(new HomeViewModel());

        var today = DateTime.Today;
        return View(new HomeViewModel
        {
            Allowance = await allowance.GetAsync(userId, today),            ActiveGoal = await goals.GetActiveAsync(userId),
            Celebration = await goals.GetCelebrationGoalAsync(userId, today),
            Pending = await monthCloses.GetPendingAsync(userId, today),
            Projection = await forecast.GetProjectedBalanceAsync(userId, today),
            GoalForecast = await forecast.GetGoalForecastAsync(userId, today),
            Totals = await transactions.GetTotalsAsync(userId, today.Year, today.Month),
            Recent = await transactions.GetRecentAsync(userId, RecentCount),
            Upcoming = await recurring.GetPendingAsync(userId, today)
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
