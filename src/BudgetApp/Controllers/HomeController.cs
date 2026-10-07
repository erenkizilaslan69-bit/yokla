using System.Diagnostics;
using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

public class HomeController(IGoalService goals, IMonthCloseService monthCloses, IForecastService forecast) : Controller
{
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return View(new HomeViewModel());

        var today = DateTime.Today;
        return View(new HomeViewModel
        {
            ActiveGoal = await goals.GetActiveAsync(userId),
            Pending = await monthCloses.GetPendingAsync(userId, today),
            Projection = await forecast.GetProjectedBalanceAsync(userId, today),
            GoalForecast = await forecast.GetGoalForecastAsync(userId, today)
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
