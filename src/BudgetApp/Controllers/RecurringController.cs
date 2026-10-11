using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Controllers;

[Authorize]
public class RecurringController(IRecurringTransactionService recurring, ICategoryService categories) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index()
    {
        var today = DateTime.Today;
        return View(new RecurringIndexViewModel
        {
            Items = await recurring.GetAllAsync(UserId, today),
            Summary = await recurring.GetSummaryAsync(UserId, today)
        });
    }

    public async Task<IActionResult> Create()
    {
        var today = DateTime.Today;
        var model = new RecurringFormViewModel { DayOfMonth = today.Day, StartDate = today };
        await FillCategoriesAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RecurringFormViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await recurring.CreateAsync(UserId, ToEntity(model), DateTime.Today);
                TempData["Message"] = "Tekrarlayan işlem kaydedildi.";
                return RedirectToAction(nameof(Index));
            }
            catch (ArgumentException)
            {
                // Amounts, days and dates are validated by the model; only the category can still mismatch.
                ModelState.AddModelError(nameof(model.CategoryId), "Kategori seçilen türle uyumlu değil.");
            }
        }

        await FillCategoriesAsync(model);
        return View(model);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await recurring.GetAsync(UserId, id);
        if (entity is null)
            return NotFound();

        var model = new RecurringFormViewModel
        {
            Id = entity.Id,
            Type = entity.Type,
            Amount = entity.Amount,
            CategoryId = entity.CategoryId,
            DayOfMonth = entity.DayOfMonth,
            ItemName = entity.ItemName,
            Note = entity.Note,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate
        };
        await FillCategoriesAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RecurringFormViewModel model)
    {
        if (id != model.Id)
            return NotFound();

        var entity = await recurring.GetAsync(UserId, id);
        if (entity is null)
            return NotFound();

        // The start date is not editable; show it again and check the end date against it.
        model.StartDate = entity.StartDate;
        if (model.EndDate is { } end && end.Date < entity.StartDate.Date)
            ModelState.AddModelError(nameof(model.EndDate), "Bitiş tarihi başlangıçtan önce olamaz.");

        if (ModelState.IsValid)
        {
            try
            {
                await recurring.UpdateAsync(UserId, ToEntity(model), DateTime.Today);
                TempData["Message"] = "Değişiklikler kaydedildi; bundan sonra oluşacak işlemlere uygulanır.";
                return RedirectToAction(nameof(Index));
            }
            catch (ArgumentException)
            {
                ModelState.AddModelError(nameof(model.CategoryId), "Kategori seçilen türle uyumlu değil.");
            }
        }

        await FillCategoriesAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(int id)
    {
        if (!await recurring.PauseAsync(UserId, id))
            return NotFound();

        TempData["Message"] = "Tekrarlayan işlem durduruldu. Devam ettirene kadar yeni işlem oluşmaz.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(int id)
    {
        if (!await recurring.ResumeAsync(UserId, id, DateTime.Today))
            return NotFound();

        TempData["Message"] = "Tekrarlayan işlem devam ediyor. Durduğu ayların işlemleri sonradan eklenmez.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var entity = await recurring.GetAsync(UserId, id);
        return entity is null ? NotFound() : View(entity);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        if (!await recurring.DeleteAsync(UserId, id))
            return NotFound();

        TempData["Message"] = "Tekrarlayan işlem silindi. Daha önce oluşan işlemler yerinde duruyor.";
        return RedirectToAction(nameof(Index));
    }

    private static RecurringTransaction ToEntity(RecurringFormViewModel model) => new()
    {
        Id = model.Id,
        Type = model.Type,
        Amount = model.Amount!.Value,
        CategoryId = model.CategoryId,
        DayOfMonth = model.DayOfMonth,
        ItemName = model.ItemName,
        Note = model.Note,
        StartDate = model.StartDate ?? DateTime.Today,
        EndDate = model.EndDate
    };

    private async Task FillCategoriesAsync(RecurringFormViewModel model)
    {
        var list = await categories.GetByTypeAsync(model.Type);
        model.Categories = list.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }
}
