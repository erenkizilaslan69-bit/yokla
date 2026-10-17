using System.Security.Claims;
using System.Text.Json;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace BudgetApp.Controllers;

/// <summary>
/// "Söyle ya da yaz": a spoken or typed sentence becomes transactions. Clear lines are saved at once (with "Geri al"
/// and "Düzenle" in the message); anything doubtful goes to a confirmation screen. The recording is never stored.
/// </summary>
[Authorize]
public class QuickEntryController(
    IQuickEntryService quickEntry, ICategoryService categories, IWarningService warningService,
    IOptions<QuickEntryOptions> options) : Controller
{
    /// <summary>Room for the largest accepted recording plus the form around it.</summary>
    private const long RequestLimit = 3_000_000;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index()
    {
        if (!quickEntry.IsEnabled)
            return NotFound();

        return View(await PageAsync(null, null));
    }

    /// <param name="text">What the user typed.</param>
    /// <param name="audio">The recording; when there is one, the text is ignored.</param>
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(RequestLimit), RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> Index(string? text, IFormFile? audio)
    {
        if (!quickEntry.IsEnabled)
            return NotFound();

        QuickEntryInput input;
        if (audio is { Length: > 0 })
        {
            if (audio.Length > options.Value.MaxAudioBytes)
                return View(await PageAsync(QuickEntryMessages.For(QuickEntryErrorKind.InvalidAudio), text));

            // In memory only: the recording is passed on and forgotten when the request ends.
            using var buffer = new MemoryStream((int)audio.Length);
            await audio.CopyToAsync(buffer, HttpContext.RequestAborted);
            input = QuickEntryInput.FromAudio(new QuickEntryAudio(buffer.ToArray(), audio.ContentType ?? ""));
        }
        else
            input = QuickEntryInput.FromText(text ?? "");

        QuickEntryProposal proposal;
        try
        {
            proposal = await quickEntry.ParseAsync(UserId, input, DateTime.Now, HttpContext.RequestAborted);
        }
        catch (QuickEntryException e)
        {
            return View(await PageAsync(e.Message, text));
        }

        if (proposal.AutoSave)
        {
            try
            {
                var lines = proposal.Lines
                    .Select(l => new QuickEntrySaveLine(l.Description, l.Amount!.Value, l.Type, l.CategoryId, l.Date))
                    .ToList();
                var saved = await quickEntry.SaveAsync(UserId, lines);
                return await AfterSaveAsync(saved, proposal.Heard);
            }
            catch (ArgumentException)
            {
                // The rules let it through but saving did not: let the user look at it.
            }
        }

        var form = QuickEntryReviewForm.From(proposal);
        await FillCategoriesAsync(form);
        return View("Review", form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(QuickEntryReviewForm form)
    {
        if (!quickEntry.IsEnabled)
            return NotFound();

        var all = (await categories.GetAllAsync()).ToDictionary(c => c.Id);
        var chosen = new List<QuickEntrySaveLine>();
        for (var i = 0; i < form.Lines.Count; i++)
        {
            var row = form.Lines[i];
            if (!row.Include)
                continue;

            if (string.IsNullOrWhiteSpace(row.Name))
                ModelState.AddModelError($"Lines[{i}].Name", "Açıklama yaz.");
            if (row.Amount is not > 0)
                ModelState.AddModelError($"Lines[{i}].Amount", "Tutar 0'dan büyük olmalı.");
            if (!all.TryGetValue(row.CategoryId, out var category))
                ModelState.AddModelError($"Lines[{i}].CategoryId", "Bir kategori seç.");
            if (row.Date is not { } date)
                ModelState.AddModelError($"Lines[{i}].Date", "Tarihi gir.");
            else if (date.Date > DateTime.Today)
                ModelState.AddModelError($"Lines[{i}].Date", "Tarih ileride olamaz.");

            if (category is not null)
                chosen.Add(new QuickEntrySaveLine(row.Name ?? "", row.Amount ?? 0m, category.Type, row.CategoryId, row.Date ?? DateTime.Today));
        }
        if (form.Lines.All(l => !l.Include))
            ModelState.AddModelError("", "Kaydetmek için en az bir satır seç.");

        if (ModelState.IsValid)
        {
            try
            {
                var saved = form.IsEdit
                    ? await quickEntry.ReplaceAsync(UserId, form.EditIds, chosen)
                    : await quickEntry.SaveAsync(UserId, chosen);
                return await AfterSaveAsync(saved, form.Heard);
            }
            catch (ArgumentException)
            {
                ModelState.AddModelError("", "Satırlar kaydedilemedi. Tarihleri, tutarları ve kategorileri kontrol et.");
            }
        }

        await FillCategoriesAsync(form);
        return View("Review", form);
    }

    /// <summary>"Geri al" in the message after saving.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Undo(List<int> ids, string? returnUrl)
    {
        var undone = await quickEntry.UndoAsync(UserId, ids);
        if (undone > 0)
            TempData["Message"] = undone == 1 ? "1 işlem geri alındı." : $"{undone} işlem geri alındı.";
        else
            TempData["Notice"] = "Geri alınacak işlem bulunamadı; zaten silinmiş olabilir.";

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Transactions");
    }

    /// <summary>"Düzenle" in the message after saving: the saved lines on the confirmation screen.</summary>
    public async Task<IActionResult> Edit(List<int> ids)
    {
        var saved = await quickEntry.GetSavedAsync(UserId, ids);
        if (saved.Count == 0)
        {
            TempData["Notice"] = "Düzenlenecek işlem bulunamadı; silinmiş olabilir.";
            return RedirectToAction("Index", "Transactions");
        }

        var form = QuickEntryReviewForm.ForEdit(saved);
        await FillCategoriesAsync(form);
        return View("Review", form);
    }

    // ---------- helpers ----------

    /// <summary>The usual warnings (once per message), the message with "Geri al" and "Düzenle", then the month's list.</summary>
    private async Task<IActionResult> AfterSaveAsync(List<Transaction> saved, string? heard)
    {
        var warnings = new List<string>();
        foreach (var transaction in saved)
            warnings.AddRange((await warningService.CheckAsync(UserId, transaction, DateTime.Today)).Select(w => w.Message));
        if (warnings.Count > 0)
            TempData[TransactionsController.WarningsKey] = JsonSerializer.Serialize(warnings.Distinct());

        TempData[QuickEntryFlash.Key] = new QuickEntryFlash(saved.Select(t => t.Id).ToList(),
            QuickEntryRules.Summary(saved), string.IsNullOrWhiteSpace(heard) ? null : heard).ToJson();

        var latest = saved.Max(t => t.Date);
        return RedirectToAction("Index", "Transactions", new { year = latest.Year, month = latest.Month });
    }

    private async Task<QuickEntryViewModel> PageAsync(string? error, string? text) => new()
    {
        Quota = await quickEntry.GetQuotaAsync(UserId, DateTime.Now),
        Error = error,
        Text = text,
        MaxRecordingSeconds = options.Value.MaxRecordingSeconds,
        MaxAudioBytes = options.Value.MaxAudioBytes,
        MaxTextLength = options.Value.MaxTextLength
    };

    private async Task FillCategoriesAsync(QuickEntryReviewForm form)
    {
        var expense = new SelectListGroup { Name = "Gider" };
        var income = new SelectListGroup { Name = "Gelir" };
        var list = await categories.GetAllAsync();
        form.Categories = list
            .OrderBy(c => c.Type == TransactionType.Expense ? 0 : 1)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())
            {
                Group = c.Type == TransactionType.Expense ? expense : income
            })
            .ToList();
    }
}
