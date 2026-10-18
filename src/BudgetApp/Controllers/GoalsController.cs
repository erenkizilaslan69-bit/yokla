using System.Security.Claims;
using BudgetApp.Infrastructure;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public class GoalsController(IGoalService goals, AppClock clock) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index() => View(await goals.GetAllAsync(UserId));

    public IActionResult Create() => View(new GoalFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GoalFormViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await goals.CreateAsync(UserId, model.Name, model.TargetAmount!.Value, clock.Now);
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Puts money into a goal by hand ("Para ekle"); the result is reported through TempData.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMoney(int id, decimal? amount, string? returnTo)
    {
        // Only two fixed destinations, never a free-form URL.
        IActionResult back = returnTo == "home" ? RedirectToAction("Index", "Home") : RedirectToAction(nameof(Index));

        if (amount is null or <= 0)
        {
            TempData["Error"] = "Geçerli bir tutar gir; tutar 0'dan büyük olmalı.";
            return back;
        }

        try
        {
            var result = await goals.AddManualContributionAsync(UserId, id, amount.Value, clock.Today);
            if (result is null)
                return NotFound();

            var message = $"{result.Goal.Name} hedefine {result.Added:C2} eklendi.";
            if (result.Completed)
                message += " Tebrikler, hedefe ulaştın!";
            TempData["Message"] = message;

            if (result.WasCapped)
                TempData["Notice"] = $"Hedefe sadece {result.Added:C2} kaldığı için {result.Requested:C2} yerine {result.Added:C2} eklendi.";
        }
        catch (ArgumentException)
        {
            TempData["Error"] = "Geçerli bir tutar gir; tutar 0'dan büyük olmalı.";
        }
        catch (InvalidOperationException)
        {
            TempData["Error"] = "Bu hedef zaten tamamlanmış.";
        }

        return back;
    }
}
