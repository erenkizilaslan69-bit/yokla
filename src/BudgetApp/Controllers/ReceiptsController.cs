using System.Security.Claims;
using BudgetApp.Infrastructure;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace BudgetApp.Controllers;

/// <summary>Receipt reading: upload a photo, check the lines, save them as expenses. The photo is never stored.</summary>
[Authorize]
public class ReceiptsController(
    IReceiptService receipts, ICategoryService categories, IWarningService warningService,
    IOptions<ReceiptOptions> options) : Controller
{
    /// <summary>Room for a 4 MB picture plus the form around it.</summary>
    private const long RequestLimit = 6_000_000;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Scan()
    {
        if (!receipts.IsEnabled)
            return NotFound();

        return View(await ScanModelAsync(null));
    }

    /// <param name="image">From the camera.</param>
    /// <param name="gallery">From the gallery; whichever of the two holds a picture is used.</param>
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(RequestLimit), RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> Scan(IFormFile? image, IFormFile? gallery)
    {
        if (!receipts.IsEnabled)
            return NotFound();

        var file = image is { Length: > 0 } ? image : gallery is { Length: > 0 } ? gallery : null;
        if (file is null)
            return View(await ScanModelAsync("Önce fişin fotoğrafını çek ya da galeriden seç."));
        if (file.Length > options.Value.MaxUploadBytes)
            return View(await ScanModelAsync(ReceiptMessages.For(ReceiptErrorKind.InvalidFile)));

        // In memory only: the picture is passed on and forgotten when the request ends.
        byte[] data;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, HttpContext.RequestAborted);
            data = buffer.ToArray();
        }

        try
        {
            var review = await receipts.ScanAsync(UserId, data, DateTime.Now, HttpContext.RequestAborted);
            var form = ReceiptReviewForm.From(review);
            await FillCategoriesAsync(form);
            return View("Review", form);
        }
        catch (ReceiptParseException e)
        {
            return View(await ScanModelAsync(e.Message));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ReceiptReviewForm form)
    {
        if (!receipts.IsEnabled)
            return NotFound();

        var rows = form.Lines.Select(l => new ReviewRow(l.Include, l.Name, l.Amount, l.CategoryId)).ToList();
        var check = ReviewLines.Check(rows, await categories.GetAllAsync(), ReviewLines.Receipt, DateTime.Today);
        foreach (var error in check.Errors)
            ModelState.AddModelError(error.Line < 0 ? "" : $"Lines[{error.Line}].{error.Field}", error.Message);

        if (ModelState.IsValid)
        {
            try
            {
                var chosen = check.Chosen.Select(c => new ReceiptSaveLine(c.Row.Name!, c.Row.Amount!.Value, c.Category.Id)).ToList();
                var saved = await receipts.SaveAsync(UserId, new ReceiptSaveInput(form.Date!.Value, form.StoreName, chosen));
                WarningsFlash.Set(TempData, await warningService.CheckManyAsync(UserId, saved, DateTime.Today));

                TempData["Message"] = saved.Count == 1 ? "Fişten 1 işlem eklendi." : $"Fişten {saved.Count} işlem eklendi.";
                var date = form.Date.Value;
                return RedirectToAction("Index", "Transactions", new { year = date.Year, month = date.Month });
            }
            catch (ArgumentException)
            {
                ModelState.AddModelError("", "Satırlar kaydedilemedi. Tarihi, tutarları ve kategorileri kontrol et.");
            }
        }

        await FillCategoriesAsync(form);
        return View("Review", form);
    }

    private async Task<ReceiptScanViewModel> ScanModelAsync(string? error) => new()
    {
        Quota = await receipts.GetQuotaAsync(UserId, DateTime.Now),
        Error = error,
        MaxImageEdge = options.Value.MaxImageEdge,
        MaxUploadBytes = options.Value.MaxUploadBytes
    };

    private async Task FillCategoriesAsync(ReceiptReviewForm form)
    {
        var list = await categories.GetByTypeAsync(TransactionType.Expense);
        form.Categories = list.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }
}
