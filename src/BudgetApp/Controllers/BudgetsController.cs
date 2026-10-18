using System.Security.Claims;
using BudgetApp.Infrastructure;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Controllers;

[Authorize]
public class BudgetsController(IBudgetService budgets, ICategoryService categories, AppClock clock) : Controller
{
    /// <summary>The form is a nested part of the page model (<c>Form.CategoryId</c> ...), so it binds with this prefix.</summary>
    private const string FormPrefix = nameof(BudgetsIndexViewModel.Form);

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <param name="categoryId">Pre-selects a category (and its current limit) in the form: the "edit" link of a row.</param>
    public async Task<IActionResult> Index(int? categoryId)
    {
        var form = new BudgetFormViewModel();
        var items = await LoadItemsAsync();
        if (categoryId is { } id && items.FirstOrDefault(i => i.Budget.CategoryId == id) is { } existing)
        {
            form.CategoryId = id;
            form.MonthlyLimit = existing.Limit;
        }

        return View(await BuildViewModelAsync(form, items));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([Bind(Prefix = FormPrefix)] BudgetFormViewModel form)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await budgets.SetAsync(UserId, form.CategoryId, form.MonthlyLimit!.Value);
                TempData["Message"] = "Bütçe kaydedildi.";
                return RedirectToAction(nameof(Index));
            }
            catch (ArgumentException)
            {
                // The amount is validated by the model; only a non-expense or unknown category can still fail.
                ModelState.AddModelError($"{FormPrefix}.{nameof(form.CategoryId)}", "Sadece gider kategorilerine limit konabilir.");
            }
        }

        return View(nameof(Index), await BuildViewModelAsync(form, await LoadItemsAsync()));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await budgets.DeleteAsync(UserId, id))
            return NotFound();

        TempData["Message"] = "Bütçe silindi.";
        return RedirectToAction(nameof(Index));
    }

    private Task<List<BudgetProgress>> LoadItemsAsync()
    {
        var today = clock.Today;
        return budgets.GetProgressAsync(UserId, today.Year, today.Month);
    }

    private async Task<BudgetsIndexViewModel> BuildViewModelAsync(BudgetFormViewModel form, List<BudgetProgress> items)
    {
        var expenseCategories = await categories.GetByTypeAsync(TransactionType.Expense);
        form.Categories = expenseCategories.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();

        var today = clock.Today;
        return new BudgetsIndexViewModel { Items = items, Form = form, Month = new DateTime(today.Year, today.Month, 1) };
    }
}
