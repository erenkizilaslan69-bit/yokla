using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public class GoalsController(IGoalService goals) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index() => View(await goals.GetAllAsync(UserId));

    public IActionResult Create() => View(new GoalFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GoalFormViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await goals.CreateAsync(UserId, model.Name, model.TargetAmount!.Value, DateTime.Now);
        return RedirectToAction("Index", "Home");
    }
}
