using System.Security.Claims;
using System.Text.Json;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Controllers;

[Authorize]
public class TransactionsController(
    ITransactionService transactions, ICategoryService categories, IWarningService warningService,
    IRecurringTransactionService recurring, ITransactionCsvExporter csvExporter) : Controller
{
    /// <summary>TempData key of the warnings shown above the list (also filled after a receipt is saved).</summary>
    internal const string WarningsKey = "Warnings";

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index(int? year, int? month)
    {
        var today = DateTime.Today;
        var (y, m) = MonthPickerViewModel.Resolve(year, month, today);

        return View(new TransactionsIndexViewModel
        {
            Year = y,
            Month = m,
            IsCurrentMonth = y == today.Year && m == today.Month,
            Transactions = await transactions.GetByMonthAsync(UserId, y, m),
            Totals = await transactions.GetTotalsAsync(UserId, y, m),
            Warnings = TakeWarnings()
        });
    }

    /// <summary>Downloads the shown month as a CSV file for Turkish Excel.</summary>
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportCsv(int? year, int? month)
    {
        var (y, m) = MonthPickerViewModel.Resolve(year, month, DateTime.Today);
        var bytes = await csvExporter.ExportMonthAsync(UserId, y, m);
        return File(bytes, "text/csv; charset=utf-8", $"islemler-{y}-{m:00}.csv");
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
        if (ModelState.IsValid)
        {
            var created = await TrySaveAsync(model, isNew: true);
            if (created is not null)
            {
                if (model.RepeatMonthly)
                {
                    // "Her ay tekrarla": this transaction is the first occurrence of a new monthly definition.
                    await recurring.CreateFromTransactionAsync(UserId, created.Id, DateTime.Today);
                    TempData["Message"] = $"Bu işlem her ayın {created.Date.Day}. günü otomatik eklenecek.";
                }

                // Warnings are shown right after an expense is saved, above the transaction list.
                var warnings = await warningService.CheckAsync(UserId, created, DateTime.Today);
                if (warnings.Count > 0)
                    TempData[WarningsKey] = JsonSerializer.Serialize(warnings.Select(w => w.Message));
                return RedirectToMonthOf(model);
            }
        }

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
            if (await TrySaveAsync(model, isNew: false) is not null)
                return RedirectToMonthOf(model);
        }

        await FillCategoriesAsync(model);
        return View(model);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var entity = await transactions.GetAsync(UserId, id);
        if (entity is null)
            return NotFound();

        // For a generated transaction the confirmation page explains what deleting it means for the definition.
        if (entity.RecurringTransactionId is { } definitionId)
            ViewBag.Recurring = await recurring.GetAsync(UserId, definitionId);
        return View(entity);
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

    /// <summary>Saves the form; returns the persisted transaction, or null when validation failed.</summary>
    private async Task<Transaction?> TrySaveAsync(TransactionFormViewModel model, bool isNew)
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
                return await transactions.CreateAsync(UserId, input);

            await transactions.UpdateAsync(UserId, input);
            return input;
        }
        catch (ArgumentException)
        {
            // Amount, date and required fields are already validated by the model; only the category can still mismatch.
            ModelState.AddModelError(nameof(model.CategoryId), "Kategori seçilen türle uyumlu değil.");
            return null;
        }
    }

    /// <summary>After saving, show the month the transaction belongs to so it is visible in the list.</summary>
    private RedirectToActionResult RedirectToMonthOf(TransactionFormViewModel model) =>
        RedirectToAction(nameof(Index), new { year = model.Date!.Value.Year, month = model.Date.Value.Month });

    private List<string> TakeWarnings() =>
        TempData[WarningsKey] is string json ? JsonSerializer.Deserialize<List<string>>(json) ?? [] : [];

    private async Task FillCategoriesAsync(TransactionFormViewModel model)
    {
        var list = await categories.GetByTypeAsync(model.Type);
        model.Categories = list.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }
}
