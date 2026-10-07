using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Controllers;

[Authorize]
public class TransactionsController(ITransactionService transactions, ICategoryService categories) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index()
    {
        ViewBag.Totals = await transactions.GetTotalsAsync(UserId);
        return View(await transactions.GetAllAsync(UserId));
    }

    public async Task<IActionResult> Create()
    {
        var model = new TransactionFormViewModel();
        await FillCategoriesAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransactionFormViewModel model)
    {
        if (ModelState.IsValid && await TrySaveAsync(model, isNew: true))
            return RedirectToAction(nameof(Index));

        await FillCategoriesAsync(model);
        return View(model);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await transactions.GetAsync(UserId, id);
        if (entity is null)
            return NotFound();

        var model = new TransactionFormViewModel
        {
            Id = entity.Id,
            Type = entity.Type,
            Amount = entity.Amount,
            Date = entity.Date,
            CategoryId = entity.CategoryId,
            ItemName = entity.ItemName,
            Note = entity.Note
        };
        await FillCategoriesAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TransactionFormViewModel model)
    {
        if (id != model.Id)
            return NotFound();

        if (ModelState.IsValid)
        {
            if (await transactions.GetAsync(UserId, id) is null)
                return NotFound();
            if (await TrySaveAsync(model, isNew: false))
                return RedirectToAction(nameof(Index));
        }

        await FillCategoriesAsync(model);
        return View(model);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var entity = await transactions.GetAsync(UserId, id);
        return entity is null ? NotFound() : View(entity);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        return await transactions.DeleteAsync(UserId, id) ? RedirectToAction(nameof(Index)) : NotFound();
    }

    [HttpGet]
    public async Task<IActionResult> CategoriesByType(TransactionType type)
    {
        var list = await categories.GetByTypeAsync(type);
        return Json(list.Select(c => new { id = c.Id, name = c.Name }));
    }

    private async Task<bool> TrySaveAsync(TransactionFormViewModel model, bool isNew)
    {
        var input = new Transaction
        {
            Id = model.Id,
            Amount = model.Amount!.Value,
            Type = model.Type,
            Date = model.Date!.Value,
            CategoryId = model.CategoryId,
            ItemName = model.ItemName,
            Note = model.Note
        };

        try
        {
            if (isNew)
                await transactions.CreateAsync(UserId, input);
            else
                await transactions.UpdateAsync(UserId, input);
            return true;
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(model.CategoryId), "Kategori seçilen türle uyumlu değil.");
            return false;
        }
    }

    private async Task FillCategoriesAsync(TransactionFormViewModel model)
    {
        var list = await categories.GetByTypeAsync(model.Type);
        model.Categories = list.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }
}
